using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using global::Signals.Game;
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

        private readonly Dictionary<string,RoutePlan> reservationContexts = new Dictionary<string,RoutePlan>();
        // A route may be accepted after its consist has already moved past the
        // first signal block. Keep the tracks observed on earlier polls so
        // automatic release can only remove a block that this route has
        // actually occupied. Without this history, a sparse first sample could
        // make an unvisited block look "behind" the current consist.
        private readonly Dictionary<string,HashSet<string>> occupiedHistory = new Dictionary<string,HashSet<string>>();
        private bool OwnsFor(string route,DVSignal signal) => ownedReservations.ContainsKey(signal) &&
            routeSignals.TryGetValue(route,out var members) && members.Contains(signal);
        private bool CanReuse(RoutePlan plan,DVSignal signal) =>
            !pendingCancellations.ContainsKey(signal) && ReservationMatches(signal) && ownedReservations.ContainsKey(signal) &&
            routeSignals.Where(p=>p.Key!=plan.id&&p.Value.Contains(signal)).All(p=>
                reservationContexts.TryGetValue(p.Key,out var context)&&ReservationCompatibility.CanShare(plan,context));
        private bool DetachShared(string route,DVSignal signal)
        {
            var remaining=routeSignals.Where(p=>p.Key!=route && p.Value.Contains(signal)).Select(p=>p.Key).ToArray();
            if(remaining.Length==0)return false;
            if(routeSignals.TryGetValue(route,out var members))members.Remove(signal);
            if(ownedReservations.TryGetValue(signal,out var owner)&&owner==route)ownedReservations[signal]=remaining[0];
            return true;
        }
        private Func<int,bool,bool> publishReservation;
        private Harmony reservationHarmony;
        private static Adapter guardOwner;
        private bool signalPacketGuard;
        private readonly HashSet<DVSignal> protectedSignals=new HashSet<DVSignal>();
        private readonly Dictionary<DVSignal,HashSet<RailTrack>> reservedPaths=new Dictionary<DVSignal,HashSet<RailTrack>>();
        private readonly Dictionary<DVSignal,string> reservedBlockKeys=new Dictionary<DVSignal,string>();
        private readonly Dictionary<string,string> invalidatedReservations=new Dictionary<string,string>();
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
            __result=guardOwner.ReservationMatches(signal);
            if(!__result) {
                string issue=TrackReserver.HasReservation(signal)?"SIGNAL_PROTECTION_CHANGED":"RESERVATION_LOST";
                guardOwner.LogReservationFailure(null,signal,"native-update:"+issue,route);
                var consumers=guardOwner.routeSignals.Where(p=>p.Value.Contains(signal)).Select(p=>p.Key).ToArray();
                foreach(var consumer in consumers) {
                    guardOwner.ReleaseRoute(consumer,guardOwner.publishReservation);
                    guardOwner.invalidatedReservations[consumer]=issue;
                }
            }
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
            if(members.Any(s=>ownedReservations.ContainsKey(s)||pendingCancellations.TryGetValue(s,out var pendingOwner)&&pendingOwner==route))return;
            routeSignals.Remove(route);
            reservationContexts.Remove(route);
            occupiedHistory.Remove(route);
        }
        private void ReservationCleared(DVSignal signal)
        {
            ownedReservations.TryGetValue(signal,out var route);
            ownedReservations.Remove(signal);
            protectedSignals.Remove(signal);reservedPaths.Remove(signal);reservedBlockKeys.Remove(signal);
            foreach(var consumer in routeSignals.Where(p=>p.Value.Contains(signal)).Select(p=>p.Key).ToArray()) {
                if(!pendingCancellations.TryGetValue(signal,out var pending)||pending!=consumer) {
                    routeSignals[consumer].Remove(signal);
                    invalidatedReservations[consumer]="RESERVATION_LOST";
                }
                PruneReleasedRoute(consumer);
            }
            PruneReleasedRoute(route);Dirty(signal);
        }
        private void ReservationMade(DVSignal signal)
        {
            pendingCancellations.TryGetValue(signal,out var oldPending);
            pendingCancellations.Remove(signal);
            if(oldPending!=null && routeSignals.TryGetValue(oldPending,out var staleMembers))staleMembers.Remove(signal);
            PruneReleasedRoute(oldPending);
            // ADS assigns ownership after ReserveForSignal returns. A later native
            // creation is a replacement, including ClearAll followed by a new reserve.
            if(ownedReservations.ContainsKey(signal))ReservationCleared(signal);
            else Dirty(signal);
        }
        public string ReserveRoute(RoutePlan plan, Func<int,bool,bool> publish, out string[] acquired, Func<string> validateFootprint = null)
        {
            acquired=new string[0]; if(!Ready)return "CAPABILITY_UNAVAILABLE";
            if(routeSignals.ContainsKey(plan.id)) {
                if(RouteReservationsIntact(plan.id))return "RESERVATION_CONFLICT";
                var cleanup=ReleaseRoute(plan.id,publish);if(cleanup!=null)return cleanup;
            }
            if(plan.reservationMode=="protected"&&global::Signals.Game.MultiplayerIntegration.IsMpRunning&&!EnsureSignalPacketGuard())return "SIGNALS_SYNC_UNAVAILABLE";
            var wanted=CurrentRouteSignals(plan,out var resolutionError);
            if(resolutionError!=null)return resolutionError;
            foreach(var signal in wanted)
            {
                if(!signal.AllowReserving)return "RESERVATION_UNAVAILABLE";
                if((TrackReserver.HasReservation(signal)&&!CanReuse(plan,signal))||TrackReserver.IsSignalReservedByAnother(signal)||HasForeignTrackOwner(signal))return "RESERVATION_CONFLICT";
            }
            plan.reservedSignals=wanted.Select(s=>"s"+s.Id).ToArray();
            var footprintError=RefreshReservationFootprint(plan,wanted,validateFootprint);
            if(footprintError!=null)return footprintError;
            publishReservation=publish;
            invalidatedReservations.Remove(plan.id);
            var owned=new HashSet<DVSignal>();routeSignals[plan.id]=owned;
            reservationContexts[plan.id]=plan.Snapshot();
            try
            {
                foreach(var signal in wanted)
                {
                    if(CanReuse(plan,signal)) {owned.Add(signal);continue;}
                    if(!TrackReserver.ReserveForSignal(signal)) { LogReservationFailure(plan,signal,"reserve"); ReleaseRoute(plan.id,publish);return "RESERVATION_CONFLICT"; }
                    // TrackReserver updates the native block inside ReserveForSignal.
                    // Re-check the live oriented path after that call so a switch
                    // or native refresh racing preflight cannot reserve another
                    // branch or a partial footprint.
                    var postReserveError=ValidateAcquiredBlock(signal,plan);
                    if(postReserveError!=null) {
                        LogReservationFailure(plan,signal,"post-reserve:"+postReserveError);
                        TrackReserver.ClearFromSignal(signal);
                        ReleaseRoute(plan.id,publish);
                        return postReserveError;
                    }
                    ownedReservations[signal]=plan.id;owned.Add(signal);
                    reservedPaths[signal]=new HashSet<RailTrack>(signal.Block.AllTracks);
                    reservedBlockKeys[signal]=NativeBlockKey(signal);
                    if(plan.reservationMode=="protected")protectedSignals.Add(signal);
                    // The path was already aligned by ADS with fresh safety checks. Native
                    // AlignAllSwitches would silently move additional switches outside it.
                    if(!publish(signal.Id,true)) { ReleaseRoute(plan.id,publish);return "SIGNALS_SYNC_UNAVAILABLE"; }
                    Sample(signal);
                }
                // Refresh the complete protection footprint after native
                // acquisition. UpdateBlocks may replace the block during the
                // call, so the game-side lock must cover the same live AllTracks.
                var finalFootprintError=RefreshReservationFootprint(plan,wanted,validateFootprint);
                if(finalFootprintError!=null) { ReleaseRoute(plan.id,publish);return finalFootprintError; }
                if(!RouteReservationsIntact(plan.id)) {ReleaseRoute(plan.id,publish);return "RESERVATION_LOST";}
                invalidatedReservations.Remove(plan.id);
                acquired=owned.Select(s=>"s"+s.Id).ToArray();return null;
            }
            catch { ReleaseRoute(plan.id,publish);throw; }
        }

        public string ReplaceRoute(RoutePlan plan, Func<int,bool,bool> publish, out string[] acquired, Func<bool> restoreInfrastructure = null, Func<string> validateFootprint = null)
        {
            acquired=Array.Empty<string>();
            if(!Ready)return "CAPABILITY_UNAVAILABLE";
            if(plan.reservationMode=="protected"&&global::Signals.Game.MultiplayerIntegration.IsMpRunning&&!EnsureSignalPacketGuard())return "SIGNALS_SYNC_UNAVAILABLE";
            var wanted=CurrentRouteSignals(plan,out var resolutionError);
            if(resolutionError!=null)return resolutionError;
            var footprintError=RefreshReservationFootprint(plan,wanted,validateFootprint);
            if(footprintError!=null)return footprintError;
            foreach(var signal in wanted) {
                if(!signal.AllowReserving)return "RESERVATION_UNAVAILABLE";
                foreach(var track in signal.Block.AllTracks)
                    if(TrackReserver.IsTrackReserved(track,out var owner)&&owner!=null&&(!OwnsFor(plan.id,owner)&&!(ReferenceEquals(owner,signal)&&CanReuse(plan,signal)))) {
                        LogReservationFailure(plan,signal,"replace-owner");return "RESERVATION_CONFLICT";
                    }
            }
            var old=routeSignals.TryGetValue(plan.id,out var previous)?previous.Where(s=>OwnsFor(plan.id,s)).ToArray():Array.Empty<DVSignal>();
            var oldPaths=old.ToDictionary(s=>s,s=>new HashSet<RailTrack>(reservedPaths[s]));
            var oldKeys=old.ToDictionary(s=>s,s=>reservedBlockKeys[s]);
            var oldProtected=new HashSet<DVSignal>(old.Where(protectedSignals.Contains));
            var kept=new HashSet<DVSignal>(old.Where(s=>wanted.Contains(s)&&ReservationMatches(s)));
            var oldShared=old.Where(s=>routeSignals.Any(p=>p.Key!=plan.id&&p.Value.Contains(s))).ToArray();
            reservationContexts.TryGetValue(plan.id,out var oldContext);
            if(oldShared.Any(s=>wanted.Contains(s)&&(!ReservationMatches(s)||!CanReuse(plan,s))))return "SHARED_SECTION_IN_USE";
            var added=new List<DVSignal>();
            string failure=null;
            publishReservation=publish;
            try {
                // Native TrackReserver cannot hold two owners on the same rail.
                // Resolve and preflight before releasing obsolete owned heads.
                foreach(var signal in old.Where(s=>!kept.Contains(s))) {
                    if(DetachShared(plan.id,signal))continue;
                    protectedSignals.Remove(signal);pendingCancellations[signal]=plan.id;
                    if(TrackReserver.HasReservation(signal))TrackReserver.ClearFromSignal(signal);
                    if(TrackReserver.HasReservation(signal)){failure="SIGNALS_RELEASE_FAILED";break;}
                    ReservationCleared(signal);
                    if(!publish(signal.Id,false)){failure="SIGNALS_SYNC_UNAVAILABLE";break;}
                    pendingCancellations.Remove(signal);
                }
                if(failure==null)foreach(var signal in wanted.Where(s=>!kept.Contains(s))) {
                    if(CanReuse(plan,signal)) {
                        if(!routeSignals.TryGetValue(plan.id,out var sharedMembers))routeSignals[plan.id]=sharedMembers=new HashSet<DVSignal>();
                        sharedMembers.Add(signal);added.Add(signal);continue;
                    }
                    if(!TrackReserver.ReserveForSignal(signal)){LogReservationFailure(plan,signal,"replace");failure="RESERVATION_CONFLICT";break;}
                    var postReserveError=ValidateAcquiredBlock(signal,plan);
                    if(postReserveError!=null) {
                        LogReservationFailure(plan,signal,"post-replace:"+postReserveError);
                        try { TrackReserver.ClearFromSignal(signal); } catch(Exception e) { UnityEngine.Debug.LogError("ADS RESERVATION_ROLLBACK_FAILED "+e); }
                        failure=postReserveError;break;
                    }
                    if(!routeSignals.TryGetValue(plan.id,out var members))routeSignals[plan.id]=members=new HashSet<DVSignal>();
                    members.Add(signal);ownedReservations[signal]=plan.id;
                    reservedPaths[signal]=new HashSet<RailTrack>(signal.Block.AllTracks);added.Add(signal);
                    reservedBlockKeys[signal]=NativeBlockKey(signal);
                    if(!publish(signal.Id,true)){failure="SIGNALS_SYNC_UNAVAILABLE";break;}
                }
                if(failure==null) {
                    failure=RefreshReservationFootprint(plan,wanted,validateFootprint);
                    if(failure==null) {
                        routeSignals[plan.id]=new HashSet<DVSignal>(wanted);
                        if(!RouteReservationsIntact(plan.id))failure="RESERVATION_LOST";
                    }
                }
            }
            catch(Exception e) { UnityEngine.Debug.LogError("ADS ROUTE_REPLACEMENT_FAILED "+e);failure="RESERVATION_CONFLICT"; }
            if(failure==null) {
                occupiedHistory.Remove(plan.id);
                invalidatedReservations.Remove(plan.id);reservationContexts[plan.id]=plan.Snapshot();
                foreach(var signal in wanted) { if(plan.reservationMode=="protected")protectedSignals.Add(signal);else protectedSignals.Remove(signal);Dirty(signal); }
                acquired=wanted.Select(s=>"s"+s.Id).ToArray();plan.reservedSignals=acquired;return null;
            }
            // Restore switches before rebuilding old native blocks. Never assign
            // a cached Block object or claim an unconfirmed reservation.
            bool rollback=true;
            foreach(var signal in added) {
                try {
                    if(DetachShared(plan.id,signal))continue;
                    protectedSignals.Remove(signal);pendingCancellations[signal]=plan.id;
                    if(TrackReserver.HasReservation(signal))TrackReserver.ClearFromSignal(signal);
                    if(TrackReserver.HasReservation(signal)){rollback=false;continue;}
                    ReservationCleared(signal);
                    if(publish(signal.Id,false))pendingCancellations.Remove(signal);else rollback=false;
                } catch(Exception e) {rollback=false;UnityEngine.Debug.LogError("ADS RESERVATION_ROLLBACK_FAILED "+e);}
            }
            bool restored=false;
            try {restored=restoreInfrastructure==null||restoreInfrastructure();}
            catch(Exception e) {UnityEngine.Debug.LogError("ADS RESERVATION_ROLLBACK_FAILED "+e);}
            rollback &= restored;
            foreach(var signal in old) {
                try {
                    if(oldShared.Contains(signal)) {
                        if(!ReservationMatches(signal)||NativeBlockKey(signal)!=oldKeys[signal]){rollback=false;continue;}
                        if(!routeSignals.TryGetValue(plan.id,out var sharedRestore))routeSignals[plan.id]=sharedRestore=new HashSet<DVSignal>();
                        sharedRestore.Add(signal);continue;
                    }
                    if(!TrackReserver.HasReservation(signal)&&restored) {
                        signal.Controller.UpdateBlocks();
                        if(signal.Block==null||!oldPaths[signal].SetEquals(signal.Block.AllTracks)||oldKeys[signal]!=NativeBlockKey(signal)||!TrackReserver.ReserveForSignal(signal)){rollback=false;continue;}
                        ownedReservations[signal]=plan.id;reservedPaths[signal]=oldPaths[signal];
                        reservedBlockKeys[signal]=oldKeys[signal];
                        if(!routeSignals.TryGetValue(plan.id,out var members))routeSignals[plan.id]=members=new HashSet<DVSignal>();
                        members.Add(signal);
                        if(!publish(signal.Id,true))rollback=false;
                    }
                    if(oldProtected.Contains(signal)&&ownedReservations.TryGetValue(signal,out var owner)&&owner==plan.id)protectedSignals.Add(signal);
                } catch(Exception e) {rollback=false;UnityEngine.Debug.LogError("ADS RESERVATION_ROLLBACK_FAILED "+e);}
            }
            if(routeSignals.TryGetValue(plan.id,out var retained))
                retained.RemoveWhere(s=>!ownedReservations.ContainsKey(s)&&!pendingCancellations.ContainsKey(s));
            if(oldContext!=null && routeSignals.ContainsKey(plan.id))reservationContexts[plan.id]=oldContext;
            return rollback?failure:"RESERVATION_ROLLBACK_FAILED";
        }

        private string RefreshReservationFootprint(RoutePlan plan, List<DVSignal> heads, Func<string> validate)
        {
            var footprint=heads.SelectMany(s=>s.Block.AllTracks).Select(getTrack).ToArray();
            if(footprint.Any(id=>id==null))return "TOPOLOGY_CHANGED";
            plan.reservationTracks=plan.tracks.Concat(footprint).Distinct().ToArray();
            return validate?.Invoke();
        }
        private string ValidateAcquiredBlock(DVSignal signal, RoutePlan plan)
        {
            if(signal?.Block==null)return "RESERVATION_UNAVAILABLE";
            if(signal.Block.AllTracks.Any(t=>getTrack(t)==null))return "TOPOLOGY_CHANGED";
            if(!RouteSignalMatches(signal,plan))return "SIGNAL_ROUTE_MISMATCH";
            if(!NativeOwnsBlock(signal))return "RESERVATION_LOST";
            return null;
        }
        private List<DVSignal> CurrentRouteSignals(RoutePlan plan, out string error)
        {
            var wanted=ResolveRouteSignals(plan,NativeReservationSignals(plan),out error);
            if(error!=null)return wanted;
            // A train can start its next order inside an already reserved
            // block, after passing the owning head. It must retain a consumer
            // reference even though that head is no longer a traversed signal.
            // This only borrows an intact ADS-owned native reservation. It
            // never selects a new block or grants authority from a nearby head.
            var borrowing=ownedReservations.Keys.Where(signal=>!wanted.Contains(signal)&&
                routeSignals.Any(p=>p.Key!=plan.id&&p.Value.Contains(signal))&&CanReuse(plan,signal)&&ReservedStartMatches(signal,plan)).ToArray();
            foreach(var controller in borrowing.Select(s=>s.Controller).Where(c=>c!=null).Distinct().ToArray())controller.UpdateBlocks();
            foreach(var signal in borrowing) {
                if(!CanReuse(plan,signal)||!ReservedStartMatches(signal,plan)) {
                    error=TrackReserver.HasReservation(signal)?"SIGNAL_PROTECTION_CHANGED":"RESERVATION_LOST";return new List<DVSignal>();
                }
                wanted.Add(signal);
            }
            return NormalizeNativeHeads(wanted);
        }
        private bool ReservedStartMatches(DVSignal signal,RoutePlan plan)
        {
            var tracks=signal.Block?.Tracks;
            if(tracks==null||plan.tracks==null||plan.tracks.Length==0||plan.directions?.Length!=plan.tracks.Length)return false;
            // This is reuse of a protection footprint, not discovery of new
            // movement authority. Ordering of an equivalent native array is
            // not identity. Primary role and direction remain mandatory.
            var primary=new Dictionary<string,int>();
            foreach(var track in tracks) {
                var id=getTrack(track.Track);
                int direction=track.Direction==TrackDirection.Out?1:track.Direction==TrackDirection.In?-1:0;
                if(id==null||direction==0||primary.TryGetValue(id,out var prior)&&prior!=direction)return false;
                primary[id]=direction;
            }
            if(!primary.ContainsKey(plan.tracks[0]))return false;
            for(int i=0;i<plan.tracks.Length;i++)if(primary.TryGetValue(plan.tracks[i],out var direction)&&direction!=plan.directions[i])return false;
            return true;
        }
        private List<DVSignal> ResolveRouteSignals(RoutePlan plan, IEnumerable<DVSignal> liveSignals, out string error)
        {
            error=null;
            // Resolve actual native heads after route switches were aligned.
            // The Host's reservedSignals/footprint are a preview, not an identity
            // whitelist. Controller gates select the traversed head, including
            // the single-head case; no nearest-head or head-count substitution.
            // Update every candidate before selecting heads. Native
            // UpdateBlocks may replace the TrackBlock instance after a switch
            // refresh; selecting from the pre-refresh objects was the source
            // of intermittent false mismatches.
            foreach(var controller in liveSignals.Where(s=>s!=null&&s.Controller!=null).Select(s=>s.Controller).Distinct())
                controller.UpdateBlocks();
            var applicable=liveSignals.Where(s=>IsRouteReservationHead(s)&&RouteSignalApplies(s,plan)).Distinct().ToList();
            if(applicable.Any(s=>!s.AllowReserving&&plan.reservedSignals.Contains("s"+s.Id))) {
                error="RESERVATION_UNAVAILABLE";return new List<DVSignal>();
            }
            var wanted=applicable.Where(s=>s.AllowReserving).ToList();
            // Rebuild a block holding unmapped rails once through its owner.
            // A rail still unknown afterwards requires a fresh topology.
            var stale=wanted.Where(s=>s.Block!=null&&s.Block.AllTracks.Any(t=>getTrack(t)==null)).ToArray();
            foreach(var signal in stale)signal.Block.FlagAsDirty();
            foreach(var controller in stale.Select(s=>s.Controller).Distinct())controller.UpdateBlocks();
            var selected=new List<DVSignal>();
            foreach(var signal in wanted) {
                if(signal.Block==null || signal.Block.AllTracks.Any(t=>getTrack(t)==null)) {
                    error=signal.Block==null?"RESERVATION_UNAVAILABLE":"TOPOLOGY_CHANGED";
                    LogReservationFailure(plan,signal,"resolve:"+error);
                    return new List<DVSignal>();
                }
                if(RouteSignalMatches(signal,plan)) { selected.Add(signal); continue; }

                // A signal mounted on a shared approach can expose a native
                // block for the opposite approach. It is not part of this
                // route when none of its oriented primary rails occur in the
                // route. Drop that candidate before reporting a mismatch. A
                // block which does touch the route remains a hard validation
                // failure: it indicates a branch, direction, or topology
                // inconsistency and must never be reserved blindly.
                var reason=RouteSignalMismatchReason(signal,plan);
                if(!BlockTouchesRoute(signal,plan) && reason=="BLOCK_SELECTION_MISMATCH") {
                    LogReservationFailure(plan,signal,"resolve:BLOCK_SELECTION_MISMATCH:candidate-skipped");
                    continue;
                }
                error="SIGNAL_ROUTE_MISMATCH";
                LogReservationFailure(plan,signal,"resolve:"+error+":"+reason);
                return new List<DVSignal>();
            }
            // A valid native block may extend beyond the user's destination.
            // Its full fresh Tracks+ExtraTracks are checked for occupancy and
            // ownership by RefreshReservationFootprint before any acquisition.
            return NormalizeNativeHeads(selected);
        }
        private List<DVSignal> NormalizeNativeHeads(IEnumerable<DVSignal> signals)
        {
            // Coverage, not mere intersection: dropping a partially overlapping
            // head used to silently lose its unique ExtraTracks protection.
            var result=new List<DVSignal>();
            foreach(var signal in signals.Where(IsRouteReservationHead).Where(s=>s.Block!=null)
                .OrderByDescending(s=>s.Block.AllTracks.Count()).ThenBy(s=>s.Id)) {
                if(result.Any(existing=>ReferenceEquals(existing.Controller,signal.Controller)&&
                    signal.Block.AllTracks.All(track=>existing.Block.AllTracks.Contains(track))&&
                    signal.Block.Tracks.All(t=>existing.Block.Tracks.Any(e=>e.Track==t.Track&&e.Direction==t.Direction))))continue;
                result.Add(signal);
            }
            return result;
        }
        private static bool IsRouteReservationHead(DVSignal signal) => signal != null && signal.Parent == null && !signal.IsShunting &&
            signal.Controller.Type != SignalType.Shunting &&
            !string.Equals(signal.Controller.Type.ToString(), "Distant", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signal.Controller.Type.ToString(), "Repeater", StringComparison.OrdinalIgnoreCase);
        private string NativeBlockKey(DVSignal signal)
        {
            var block=signal?.Block;
            if(block==null)return "";
            return SignalBlockIdentity.Key(block.Tracks?.Select(t=>getTrack(t.Track)).ToArray(),
                block.Tracks?.Select(t=>t.Direction==TrackDirection.Out?1:-1).ToArray(),block.ExtraTracks?.Select(getTrack).ToArray());
        }
        private void LogReservationFailure(RoutePlan plan, DVSignal signal, string stage, string routeId=null)
        {
            try
            {
                var tracks=signal?.Block?.AllTracks==null?Array.Empty<string>():signal.Block.AllTracks.Select(track=>getTrack(track)).Where(id=>id!=null).ToArray();
                var owners=signal?.Block?.AllTracks==null?Array.Empty<string>():signal.Block.AllTracks.Select(track=>
                {
                    try { return TrackReserver.IsTrackReserved(track,out var owner) && owner!=null ? getTrack(track)+"<-s"+owner.Id+":"+owner.Name : getTrack(track)+"<-free"; }
                    catch(Exception e) { return getTrack(track)+"<-query:"+e.GetType().Name; }
                }).ToArray();
                var primary=signal?.Block?.Tracks==null?Array.Empty<string>():signal.Block.Tracks.Select(t=>getTrack(t.Track)+":"+(t.Direction==TrackDirection.Out?1:-1)).ToArray();
                var extras=signal?.Block?.ExtraTracks==null?Array.Empty<string>():signal.Block.ExtraTracks.Select(getTrack).ToArray();
                string oldKey=signal!=null&&reservedBlockKeys.TryGetValue(signal,out var prior)?prior:"none";
                bool routeMatch=signal!=null&&plan!=null&&RouteSignalMatches(signal,plan);
                var placement=signal?.Controller?.PlacementInfo;
                var placementTrack=placement.HasValue?getTrack(placement.Value.Track):null;
                var placementDirection=placement.HasValue?placement.Value.Direction.ToString():"";
                var nativeStart=signal?.Controller==null?null:getTrack(NativeStartingTrack(signal.Controller));
                var nativeDirection=signal?.Controller==null?"":(NativeDirection(signal.Controller)?.ToString()??"");
                var mismatchReason=signal!=null&&plan!=null?RouteSignalMismatchReason(signal,plan):"UNKNOWN";
                var routeSegments=plan?.tracks==null?"":string.Join(";",plan.tracks.Select((id,i)=>"["+i+"]"+id+":"+(i<(plan.directions?.Length??0)?plan.directions[i].ToString():"?") ));
                var routeSwitches=plan?.switches==null?"":string.Join(",",plan.switches.Select(s=>(s?.id??"")+"#"+(s?.branch.ToString()??"?")));
                UnityEngine.Debug.LogError("ADS SIGNAL_RESERVATION_REJECTED stage="+stage+" route="+(plan?.id??routeId??"")+" from="+(plan?.from??"")+" to="+(plan?.to??"")+" via="+string.Join(",",plan?.via??Array.Empty<string>())+" routeSegments="+routeSegments+" routeSwitches="+routeSwitches+" signal="+(signal?.Id.ToString()??"")+" name="+(signal?.Name??"")+" controller="+(signal?.Controller?.Id.ToString()??"")+" controllerType="+(signal?.Controller?.GetType().Name??"")+" shunting="+(signal?.IsShunting.ToString()??"")+" parent="+(signal?.Parent?.Id.ToString()??"")+" placementTrack="+(placementTrack??"")+" placementDirection="+placementDirection+" nativeStartingTrack="+(nativeStart??"")+" nativeDirection="+nativeDirection+" nativeBlockId="+(signal?.Block?.Id.ToString()??"")+" previousFootprint="+oldKey+" currentFootprint="+NativeBlockKey(signal)+" routeMatch="+routeMatch+" mismatchReason="+mismatchReason+" path="+string.Join(",",plan?.tracks??Array.Empty<string>())+" directions="+string.Join(",",plan?.directions??Array.Empty<int>())+" requestedHeads="+string.Join(",",plan?.reservedSignals??Array.Empty<string>())+" blockTracks="+string.Join(",",primary)+" blockExtraTracks="+string.Join(",",extras)+" allTracks="+string.Join(",",tracks)+" owners="+string.Join(",",owners)+" reservedByAnother="+(signal!=null&&TrackReserver.IsSignalReservedByAnother(signal)));
            }
            catch(Exception e) { UnityEngine.Debug.LogError("ADS SIGNAL_RESERVATION_DIAGNOSTIC_FAILED "+e.GetType().Name); }
        }
        private static bool HasForeignTrackOwner(DVSignal signal)
        {
            if(signal?.Block?.AllTracks==null)return false;
            foreach(var track in signal.Block.AllTracks)
                if(TrackReserver.IsTrackReserved(track,out var owner)&&owner!=null&&!ReferenceEquals(owner,signal))return true;
            return false;
        }
        private bool NativeOwnsBlock(DVSignal signal) => signal?.Block?.AllTracks!=null && signal.Block.AllTracks.All(track =>
            TrackReserver.IsTrackReserved(track,out var owner) && ReferenceEquals(owner,signal));
        private bool ReservationMatches(DVSignal signal) => TrackReserver.HasReservation(signal) && NativeOwnsBlock(signal) && signal.Block!=null &&
            reservedPaths.TryGetValue(signal,out var path) && path.SetEquals(signal.Block.AllTracks) &&
            reservedBlockKeys.TryGetValue(signal,out var key) && key==NativeBlockKey(signal);
        public bool RouteReservationsIntact(string route) => !invalidatedReservations.ContainsKey(route) && routeSignals.TryGetValue(route,out var members) && members.All(s=>OwnsFor(route,s)&&ReservationMatches(s));
        public string RouteReservationIssue(string route)
        {
            if(invalidatedReservations.TryGetValue(route,out var issue))return issue;
            if(!routeSignals.TryGetValue(route,out var members))return "RESERVATION_LOST";
            foreach(var signal in members) {
                if(!OwnsFor(route,signal)||!TrackReserver.HasReservation(signal))return "RESERVATION_LOST";
                if(!ReservationMatches(signal)) {LogReservationFailure(null,signal,"poll:footprint-changed",route);return "SIGNAL_PROTECTION_CHANGED";}
            }
            return null;
        }
        /// <summary>
        /// Releases only native signal blocks whose complete AllTracks
        /// footprint is clear of the supplied consist tracks. Shared native
        /// reservations are detached from this route while another compatible
        /// route keeps ownership; native ClearFromSignal is called only when
        /// the last route reference is gone.
        /// </summary>
        public string ReleaseCompletedSegments(string route, string[] signalIds, string[] occupiedTracks,
            string[] orderedRouteTracks, bool reverse, Func<int, bool, bool> publish,
            out string[] releasedSignals, out string[] releasedTracks)
        {
            releasedSignals = Array.Empty<string>(); releasedTracks = Array.Empty<string>();
            if (string.IsNullOrEmpty(route) || !routeSignals.TryGetValue(route, out var members)) return null;
            var wanted = new HashSet<string>(signalIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var occupied = new HashSet<string>(occupiedTracks ?? Array.Empty<string>(), StringComparer.Ordinal);
            var ordered = orderedRouteTracks ?? Array.Empty<string>();
            if (!occupiedHistory.TryGetValue(route, out var previouslyOccupied))
            {
                previouslyOccupied = new HashSet<string>(StringComparer.Ordinal);
                occupiedHistory[route] = previouslyOccupied;
            }
            var occupiedIndices = ordered.Select((id, index) => new { id, index }).Where(x => occupied.Contains(x.id)).Select(x => x.index).ToArray();
            var done = new List<string>(); var tracks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var signal in members.ToArray())
            {
                var id = "s" + signal.Id;
                if (wanted.Count > 0 && !wanted.Contains(id) || signal.Block == null) continue;
                var blockTracks = signal.Block.AllTracks.Select(getTrack).Where(t => t != null).Distinct(StringComparer.Ordinal).ToArray();
                if (blockTracks.Length == 0 || blockTracks.Any(occupied.Contains)) continue;
                // A block is releasable only after a previous observation saw
                // this route's consist on at least one of its ordered tracks.
                // This prevents releasing future/unvisited blocks when the
                // first live sample arrives in the middle of a route.
                var entered = blockTracks.Any(previouslyOccupied.Contains);
                if (!entered) continue;
                if (occupiedIndices.Length == 0) continue;
                var blockIndices = ordered.Select((id, index) => new { id, index }).Where(x => blockTracks.Contains(x.id)).Select(x => x.index).ToArray();
                if (blockIndices.Length == 0) continue;
                // orderedRouteTracks is the route's traversal order regardless
                // of the physical rail direction. Reversing the comparison for
                // a -1 first segment released the block ahead of a reverse
                // moving consist and retained the block behind it. Keep the
                // legacy `reverse` parameter for protocol/caller compatibility,
                // but derive progress solely from ordered topology and the
                // complete consist occupancy.
                var behind = RouteProgressRules.IsBehindCurrent(ordered, blockTracks, occupied);
                if (!behind) continue;
                var error = ReleaseOwnedSignal(route, signal, publish);
                if (error != null) return error;
                done.Add(id); foreach (var track in blockTracks) tracks.Add(track);
            }
            // A physical track can belong to more than one remaining native
            // block (loops/overlapping protection). Report it as released only
            // when no remaining member of this route still covers it.
            // Other routes retain their own footprint/claim and native owner.
            // Keeping their tracks in THIS route leaked its local lock forever
            // after DetachShared. Subtract only this route's remaining heads.
            var remainingTracks = members
                .Where(s => s != null && s.Block != null)
                .SelectMany(s => s.Block.AllTracks)
                .Select(getTrack)
                .Where(t => t != null)
                .ToHashSet(StringComparer.Ordinal);
            tracks.ExceptWith(remainingTracks);
            releasedSignals = done.ToArray(); releasedTracks = tracks.ToArray();
            // Update after evaluating the previous sample: the first poll
            // establishes entry, the following poll can then release the tail
            // block once no consist footprint remains in it.
            previouslyOccupied.UnionWith(occupied);
            return null;
        }
        /// <summary>
        /// Seeds the route's visited-track history from the authoritative
        /// consist footprint captured at the moment native reservation was
        /// accepted. This covers a train that is already inside the first
        /// signal block when the route is reserved and moves beyond it before
        /// the first lifecycle poll. Only observed body tracks are accepted;
        /// an empty/unknown footprint does not authorize release.
        /// </summary>
        public void SeedRouteOccupancy(string route, IEnumerable<string> occupiedTracks)
        {
            if (string.IsNullOrEmpty(route) || occupiedTracks == null) return;
            var tracks = occupiedTracks.Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (tracks.Length == 0) return;
            if (!occupiedHistory.TryGetValue(route, out var history))
                occupiedHistory[route] = history = new HashSet<string>(StringComparer.Ordinal);
            history.UnionWith(tracks);
        }
        public string[] RouteProtectionTracks(string route)
        {
            if (string.IsNullOrEmpty(route) || !routeSignals.TryGetValue(route, out var members)) return Array.Empty<string>();
            var tracks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var signal in members)
            {
                if (signal?.Block?.AllTracks == null) return null;
                foreach (var nativeTrack in signal.Block.AllTracks)
                {
                    var id = getTrack(nativeTrack);
                    if (string.IsNullOrEmpty(id)) return null;
                    tracks.Add(id);
                }
            }
            return tracks.ToArray();
        }
        private string ReleaseOwnedSignal(string route, DVSignal signal, Func<int, bool, bool> publish)
        {
            if (OwnsFor(route, signal) && DetachShared(route, signal)) return null;
            bool ours = ownedReservations.TryGetValue(signal, out var owner) && owner == route;
            bool notifying = pendingCancellations.TryGetValue(signal, out var pendingOwner) && pendingOwner == route;
            if (!ours && !notifying) { if (routeSignals.TryGetValue(route, out var set)) set.Remove(signal); return null; }
            try
            {
                if (ours)
                {
                    protectedSignals.Remove(signal); pendingCancellations[signal] = route;
                    if (TrackReserver.HasReservation(signal)) TrackReserver.ClearFromSignal(signal);
                }
                if (TrackReserver.HasReservation(signal)) return "SIGNALS_RELEASE_FAILED";
                if (ownedReservations.TryGetValue(signal, out owner) && owner == route) ReservationCleared(signal);
                if (pendingCancellations.TryGetValue(signal, out pendingOwner) && pendingOwner == route)
                {
                    if (!(publish?.Invoke(signal.Id, false) ?? true)) return "SIGNALS_SYNC_UNAVAILABLE";
                    pendingCancellations.Remove(signal);
                }
                if (routeSignals.TryGetValue(route, out var members)) members.Remove(signal);
                Dirty(signal); return null;
            }
            catch (Exception e) { UnityEngine.Debug.LogError("ADS SIGNALS_RELEASE_FAILED " + e); return "SIGNALS_RELEASE_FAILED"; }
        }
        public string ReleaseRoute(string route, Func<int,bool,bool> publish)
        {
            invalidatedReservations.Remove(route);
            if(!routeSignals.TryGetValue(route,out var members))return null;
            string error=null;
            foreach(var signal in members.ToArray())
            {
                var signalError = ReleaseOwnedSignal(route, signal, publish);
                if (signalError != null) error ??= signalError;
            }
            if(members.Count==0) {routeSignals.Remove(route);reservationContexts.Remove(route);occupiedHistory.Remove(route);}
            return error;
        }
        private void ReleaseAllOwned()
        {
            foreach(var route in routeSignals.Keys.ToArray()) {var error=ReleaseRoute(route,publishReservation ?? ((id,reserved)=>true));if(error!=null)UnityEngine.Debug.LogError("ADS "+error+" during signal adapter cleanup");}
            reservationContexts.Clear();occupiedHistory.Clear();ownedReservations.Clear();routeSignals.Clear();pendingCancellations.Clear();protectedSignals.Clear();reservedPaths.Clear();reservedBlockKeys.Clear();invalidatedReservations.Clear();
        }
    }
}
