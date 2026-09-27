using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using global::Signals.Game.Railway;
using DVSignal = global::Signals.Game.Signal;
using HarmonyLib;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        private readonly Dictionary<DVSignal,string> pendingCancellations = new Dictionary<DVSignal,string>();
        private readonly Dictionary<DVSignal,string> ownedReservations = new Dictionary<DVSignal,string>();
        private readonly Dictionary<string, HashSet<DVSignal>> routeSignals = new Dictionary<string, HashSet<DVSignal>>();
        private Func<int,bool,bool> publishReservation;
        private Harmony reservationHarmony;
        private static Adapter guardOwner;
        private bool signalPacketGuard;
        private readonly HashSet<DVSignal> protectedSignals=new HashSet<DVSignal>();
        private readonly Dictionary<DVSignal,HashSet<RailTrack>> reservedPaths=new Dictionary<DVSignal,HashSet<RailTrack>>();
        private void InstallReservationGuards()
        {
            var patch=new Harmony("denis.ads.signals-reservations");
            try {
                patch.Patch(AccessTools.Method(typeof(TrackReserver),"ClearFromSignal",new[]{typeof(DVSignal)}),prefix:new HarmonyMethod(typeof(Adapter),nameof(GuardClear)));
                patch.Patch(AccessTools.Method(typeof(TrackReserver),"ReserveForSignal",new[]{typeof(DVSignal)}),prefix:new HarmonyMethod(typeof(Adapter),nameof(GuardReserve)));
                patch.Patch(AccessTools.Method(typeof(TrackReserver),"UpdateReservation",new[]{typeof(DVSignal)}),prefix:new HarmonyMethod(typeof(Adapter),nameof(GuardUpdate)));
                reservationHarmony=patch;guardOwner=this;
            }
            catch {patch.UnpatchAll("denis.ads.signals-reservations");throw;}
        }
        private static bool GuardClear(DVSignal signal) => guardOwner==null||!guardOwner.protectedSignals.Contains(signal);
        private static bool GuardReserve(DVSignal signal,ref bool __result)
        {
            if(GuardClear(signal))return true;__result=false;return false;
        }
        private static bool GuardUpdate(DVSignal signal,ref bool __result)
        {
            if(guardOwner==null||!guardOwner.ownedReservations.TryGetValue(signal,out var route))return true;
            __result=TrackReserver.HasReservation(signal)&&signal.Block!=null&&guardOwner.reservedPaths.TryGetValue(signal,out var path)&&path.SetEquals(signal.Block.AllTracks);
            if(!__result)guardOwner.ReleaseRoute(route,guardOwner.publishReservation);
            return false;
        }
        private bool EnsureSignalPacketGuard()
        {
            if(signalPacketGuard)return true;
            var type=AccessTools.TypeByName("Signals.MP.ServerManager");
            var target=type==null?null:AccessTools.DeclaredMethod(type,"ReservationCancelled");
            if(target==null)return false;
            reservationHarmony.Patch(target,prefix:new HarmonyMethod(typeof(Adapter),nameof(GuardCancelPacket)) {priority=Priority.First});
            signalPacketGuard=true;return true;
        }
        private static bool GuardCancelPacket(object __0)
        {
            if(guardOwner==null)return true;
            int id=(int)__0.GetType().GetProperty("SignalId").GetValue(__0,null);
            if(!guardOwner.registry.TryGetValue(id,out var signal)||GuardClear(signal))return true;
            // Do not let native cancellation broadcast success for a protected signal.
            guardOwner.publishReservation?.Invoke(id,true);return false;
        }
        private void PruneReleasedRoute(string route)
        {
            if(route==null||!routeSignals.TryGetValue(route,out var members))return;
            if(members.Any(s=>ownedReservations.TryGetValue(s,out var owner)&&owner==route||pendingCancellations.TryGetValue(s,out var pendingOwner)&&pendingOwner==route))return;
            routeSignals.Remove(route);
        }
        private void ReservationCleared(DVSignal signal)
        {
            ownedReservations.TryGetValue(signal,out var route);
            ownedReservations.Remove(signal);
            protectedSignals.Remove(signal);reservedPaths.Remove(signal);
            PruneReleasedRoute(route);Dirty(signal);
        }
        private void ReservationMade(DVSignal signal)
        {
            pendingCancellations.TryGetValue(signal,out var oldPending);
            pendingCancellations.Remove(signal);PruneReleasedRoute(oldPending);
            // ADS assigns ownership after ReserveForSignal returns. A later native
            // creation is a replacement, including ClearAll followed by a new reserve.
            if(ownedReservations.ContainsKey(signal))ReservationCleared(signal);
            else Dirty(signal);
        }
        public string ReserveRoute(RoutePlan plan, Func<int,bool,bool> publish, out string[] acquired)
        {
            acquired=new string[0]; if(!Ready)return "CAPABILITY_UNAVAILABLE";
            if(routeSignals.ContainsKey(plan.id)) {
                if(RouteReservationsIntact(plan.id))return "RESERVATION_CONFLICT";
                var cleanup=ReleaseRoute(plan.id,publish);if(cleanup!=null)return cleanup;
            }
            if(plan.reservationMode=="protected"&&global::Signals.Game.MultiplayerIntegration.IsMpRunning&&!EnsureSignalPacketGuard())return "SIGNALS_SYNC_UNAVAILABLE";
            var wanted=new List<DVSignal>(); var allowed=new HashSet<string>(plan.reservationTracks.Concat(plan.tracks));
            foreach(var id in plan.reservedSignals.Distinct())
            {
                if(id==null||!id.StartsWith("s")||!int.TryParse(id.Substring(1),out int index)||!registry.TryGetValue(index,out var signal))return "NOT_FOUND";
                if(!signal.AllowReserving)return "RESERVATION_UNAVAILABLE";
                signal.Controller.UpdateBlocks();
                if(signal.Block==null)return "RESERVATION_UNAVAILABLE";
                if(!RouteSignalMatches(signal,plan))return "SIGNAL_PATH_CHANGED";
                if(signal.Block.AllTracks.Any(t=>getTrack(t)==null||!allowed.Contains(getTrack(t))))return "SIGNAL_PATH_CHANGED";
                if(TrackReserver.HasReservation(signal)||TrackReserver.IsSignalReservedByAnother(signal))return "RESERVATION_CONFLICT";
                wanted.Add(signal);
            }
            publishReservation=publish;
            var owned=new HashSet<DVSignal>();routeSignals[plan.id]=owned;
            try
            {
                foreach(var signal in wanted)
                {
                    if(!TrackReserver.ReserveForSignal(signal)) { ReleaseRoute(plan.id,publish);return "RESERVATION_CONFLICT"; }
                    ownedReservations[signal]=plan.id;owned.Add(signal);
                    reservedPaths[signal]=new HashSet<RailTrack>(signal.Block.AllTracks);
                    if(plan.reservationMode=="protected")protectedSignals.Add(signal);
                    // The path was already aligned by ADS with fresh safety checks. Native
                    // AlignAllSwitches would silently move additional switches outside it.
                    if(!publish(signal.Id,true)) { ReleaseRoute(plan.id,publish);return "SIGNALS_SYNC_UNAVAILABLE"; }
                    Sample(signal);
                }
                if(!RouteReservationsIntact(plan.id)) {ReleaseRoute(plan.id,publish);return "RESERVATION_LOST";}
                acquired=owned.Select(s=>"s"+s.Id).ToArray();return null;
            }
            catch { ReleaseRoute(plan.id,publish);throw; }
        }
        public bool RouteReservationsIntact(string route) => routeSignals.TryGetValue(route,out var members) && members.All(s=>ownedReservations.TryGetValue(s,out var owner)&&owner==route&&TrackReserver.HasReservation(s)&&s.Block!=null&&reservedPaths.TryGetValue(s,out var path)&&path.SetEquals(s.Block.AllTracks));
        public string ReleaseRoute(string route, Func<int,bool,bool> publish)
        {
            if(!routeSignals.TryGetValue(route,out var members))return null;
            string error=null;
            foreach(var signal in members.ToArray())
            {
                bool ours=ownedReservations.TryGetValue(signal,out var owner)&&owner==route;
                bool notifying=pendingCancellations.TryGetValue(signal,out var pendingOwner)&&pendingOwner==route;
                if(!ours&&!notifying) {members.Remove(signal);continue;}
                try {
                    if(ours) {
                        // Keep ownership until native clear is observed. The clear event
                        // removes it; a replacement event also invalidates our pending sync.
                        protectedSignals.Remove(signal);
                        pendingCancellations[signal]=route;
                        if(TrackReserver.HasReservation(signal))TrackReserver.ClearFromSignal(signal);
                    }
                    if(TrackReserver.HasReservation(signal)) {
                        if(ownedReservations.TryGetValue(signal,out owner)&&owner==route)error="SIGNALS_RELEASE_FAILED";
                        else {pendingCancellations.Remove(signal);members.Remove(signal);}
                        continue;
                    }
                    if(ownedReservations.TryGetValue(signal,out owner)&&owner==route)ReservationCleared(signal);
                    if(pendingCancellations.TryGetValue(signal,out pendingOwner)&&pendingOwner==route) {
                        if(!publish(signal.Id,false)) {error="SIGNALS_SYNC_UNAVAILABLE";continue;}
                        pendingCancellations.Remove(signal);
                    }
                    members.Remove(signal);Dirty(signal);
                }
                catch(Exception e) {error="SIGNALS_RELEASE_FAILED";UnityEngine.Debug.LogError("ADS SIGNALS_RELEASE_FAILED "+e);}
            }
            if(members.Count==0)routeSignals.Remove(route);
            return error;
        }
        private void ReleaseAllOwned()
        {
            foreach(var route in routeSignals.Keys.ToArray()) {var error=ReleaseRoute(route,publishReservation ?? ((id,reserved)=>true));if(error!=null)UnityEngine.Debug.LogError("ADS "+error+" during signal adapter cleanup");}
            ownedReservations.Clear();routeSignals.Clear();pendingCancellations.Clear();protectedSignals.Clear();reservedPaths.Clear();
        }
    }
}
