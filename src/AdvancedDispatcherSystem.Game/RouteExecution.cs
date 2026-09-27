using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using HarmonyLib;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private sealed class WatchedRoute { public RoutePlan plan; public RouteRuntimeState state; public bool cleanupRequired; }
        private readonly Dictionary<string, WatchedRoute> watchedRoutes = new Dictionary<string, WatchedRoute>();
        // Original turnout positions for a route that is still being prepared.
        // They allow a rejected/unknown registration to undo only our own
        // partial alignment without touching an active route's switches.
        private readonly Dictionary<string, Dictionary<string, byte>> pendingSwitches = new Dictionary<string, Dictionary<string, byte>>();
        private readonly RouteLocks routeLocks = new RouteLocks();
        private TrackGraph executionGraph;
        private float nextRouteCheck;
        private bool routesDirty;
        public static bool ProtectedSwitchAllowed(Junction junction, int branch) => Main.Runtime == null || Main.Runtime.AllowsProtectedSwitch(junction, branch);
        // MP packet handlers use the same decision as the native switch entry point.
        public bool AllowsProtectedSwitch(Junction junction, int branch)
        {
            if (!world || Main.Config.ReadOnly || !multiplayer.Authority || junction == null) return true;
            return !junctionIds.TryGetValue(junction, out var id) || routeLocks.AllowsSwitch(id, branch);
        }
        [HarmonyPatch(typeof(Junction), "Switch", new[] { typeof(Junction.SwitchMode), typeof(byte) })]
        private static class ProtectedJunction
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Junction __instance, byte branch) => Main.Runtime == null || Main.Runtime.AllowsProtectedSwitch(__instance, __instance.outBranches.Count == 0 ? branch : branch % __instance.outBranches.Count);
        }
        private CarState[] LiveRouteCars()
        {
            var result = new List<CarState>(carList.Count);
            foreach (var entry in carList)
            {
                var car = entry.car; if (car == null) continue;
                var p = car.transform.TransformPoint(car.Bounds.center) - WorldMover.currentMove;
                result.Add(new CarState { id = entry.id, consist = car.trainset == null ? "car:" + entry.id : "train:" + car.trainset.id,
                    track1 = TrackId(car.FrontBogie?.track), track2 = TrackId(car.RearBogie?.track), span1 = car.FrontBogie?.traveller?.Span ?? 0,
                    span2 = car.RearBogie?.traveller?.Span ?? 0, length = car.InterCouplerDistance, speed = car.GetForwardSpeed() * 3.6,
                    x = p.x, z = p.z, sampledAt = Protocol.Now, derailed = car.FrontBogie?.HasDerailed == true || car.RearBogie?.HasDerailed == true });
            }
            return result.ToArray();
        }
        private string SwitchSafety(Junction junction)
        {
            var p = junction.position - WorldMover.currentMove;
            var branchTracks = new HashSet<string>(junction.outBranches.Select(b => TrackId(b?.track)).Where(t => t != null));
            branchTracks.Add(TrackId(junction.inBranch?.track));
            foreach (var c in LiveRouteCars())
            {
                var footprint = executionGraph?.Footprint(c);
                bool nearbyTrack = branchTracks.Contains(c.track1) || branchTracks.Contains(c.track2) || footprint != null && footprint.Any(branchTracks.Contains);
                if (!nearbyTrack) continue;
                double distance = Math.Sqrt((c.x-p.x)*(c.x-p.x)+(c.z-p.z)*(c.z-p.z));
                if (distance < c.length / 2 + 12 || footprint != null && footprint.Count(branchTracks.Contains) > 1) return "SWITCH_OCCUPIED";
                if (Math.Abs(c.speed) > 1 && distance < c.length / 2 + Math.Max(30, Math.Abs(c.speed) / 3.6 * 6)) return "APPROACHING_TRAIN";
            }
            return null;
        }
        private string RouteAlignment(RoutePlan plan)
        {
            foreach (var s in plan.switches)
                if (!junctionById.TryGetValue(s.id, out var j) || j == null || j.selectedBranch != s.branch) return "SWITCH_MISALIGNED";
            foreach (var step in plan.turntables)
                if (!tables.TryGetValue(step.id, out var t) || t.table == null || t.command != null || !Connected(t, new Command { from=step.from,to=step.to,fromEnd=step.fromEnd,toEnd=step.toEnd,index=step.position })) return "TURNTABLE_MISALIGNED";
            return null;
        }
        private string RouteOccupancy(RoutePlan plan, CarState[] observed)
        {
            var path = new HashSet<string>(plan.reservationTracks.Concat(plan.tracks));
            foreach (var step in plan.turntables) if (tables.TryGetValue(step.id, out var table)) path.Add(table.def.track);
            var own = new HashSet<string>(plan.trainCars);
            foreach (var c in observed)
            {
                var footprint = executionGraph?.Footprint(c) ?? new[] { c.track1, c.track2 };
                if (footprint.Any(t => t != null && path.Contains(t)) && !own.Contains(c.id)) return "TRACK_OCCUPIED";
                if (own.Contains(c.id) && (c.consist != plan.train || c.derailed)) return "TRAIN_CHANGED";
            }
            return null;
        }
        private string ReservedTable(string id,Command command)
        {
            if(!tables.TryGetValue(id??"",out var table))return null;
            var owner=routeLocks.TrackOwner(table.def.track);
            return owner!=null&&!Connected(table,command)?"ROUTE_RESERVED":null;
        }
        private void RememberRouteSwitch(Command command, Junction junction)
        {
            if (string.IsNullOrEmpty(command.routeId) || junction == null) return;
            if (!pendingSwitches.TryGetValue(command.routeId, out var originals))
                pendingSwitches[command.routeId] = originals = new Dictionary<string, byte>();
            if (!originals.ContainsKey(command.target)) originals[command.target] = junction.selectedBranch;
        }
        private string RollbackRouteSwitches(string routeId)
        {
            if (!pendingSwitches.TryGetValue(routeId ?? "", out var originals)) return null;
            string error = null;
            foreach (var entry in originals)
            {
                if (!junctionById.TryGetValue(entry.Key, out var junction) || junction == null) { error ??= "SWITCH_ROLLBACK_FAILED"; continue; }
                if (junction.selectedBranch == entry.Value) continue;
                if (!AllowsProtectedSwitch(junction, entry.Value) || SwitchSafety(junction) != null) { error ??= "SWITCH_ROLLBACK_UNSAFE"; continue; }
                var method = junction.GetType().GetMethod("Switch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                    null, new[] { typeof(Junction.SwitchMode), typeof(byte) }, null);
                if (method != null)
                    method.Invoke(junction, new object[] { Enum.Parse(typeof(Junction.SwitchMode), "FORCED"), entry.Value });
                else
                    junction.selectedBranch = entry.Value; // lightweight driver fallback; native builds always expose Switch.
                if (junction.selectedBranch != entry.Value) error ??= "SWITCH_ROLLBACK_FAILED";
            }
            pendingSwitches.Remove(routeId ?? "");
            return error;
        }
        private void RouteCommand(Command c)
        {
            CommandResult Receipt(string code = null) => new CommandResult { id=c.id,target=c.target,status=code==null?"applied":"rejected",code=code };
            if (c.kind == "releaseGameRoute" || c.kind == "endGameRoute")
            {
                if(c.kind=="endGameRoute")foreach(var table in tables.Values.Where(t=>t.command?.routeId==c.target).ToArray())FinishTable(table,"ROUTE_CANCELLED");
                string releaseError=null;
                if (c.action == "failed" || c.action == "cancelled") releaseError = RollbackRouteSwitches(c.target);
                if (watchedRoutes.TryGetValue(c.target ?? "", out var existing)) {
                    var nativeError=ReleaseWatchedRoute(existing, c.kind == "endGameRoute" ? c.action == "completed" ? "completed" : "cancelled" : existing.state.lifecycle, null);
                    releaseError ??= nativeError;
                }
                else if(!string.IsNullOrEmpty(c.target)) {
                    // A reserve can fail before registration while its rollback still owns
                    // native signals. Explicit cleanup must reach that ownership as well.
                    releaseError ??= signals?.ReleaseRoute(c.target,multiplayer.PublishSignalReservation);
                    string lifecycle=c.kind=="endGameRoute"?(c.action=="completed"?"completed":"cancelled"):
                        (c.action=="cancelled"||c.action=="completed"||c.action=="interrupted"||c.action=="failed"?c.action:"active");
                    watchedRoutes[c.target]=new WatchedRoute {plan=new RoutePlan{id=c.target},cleanupRequired=releaseError!=null,
                        state=new RouteRuntimeState{id=c.target,lifecycle=lifecycle,reservation=releaseError==null?"released":"releaseFailed",reason=releaseError,time=Protocol.Now}};
                }
                PruneWatchedRoutes();
                SendRouteStates(); Result(Receipt(releaseError)); return;
            }
            var plan = c.route;
            if (plan == null || string.IsNullOrEmpty(plan.id) || plan.id != c.target || plan.tracks == null || plan.tracks.Length == 0 || plan.tracks.Length > 4096 || plan.switches == null || plan.turntables == null || executionGraph == null) { Result(Receipt("INVALID_ROUTE")); return; }
            string mode = c.kind == "watchRoute" ? "none" : c.reservationMode;
            plan.reservationMode=mode;
            if (mode != "none" && mode != "normal" && mode != "protected") { Result(Receipt("INVALID_RESERVATION")); return; }
            if (mode == "protected" && !multiplayer.ProtectedSwitches) { Result(Receipt("PROTECTION_UNAVAILABLE")); return; }
            if(watchedRoutes.TryGetValue(plan.id,out var ended)&&ended.state.lifecycle!="active") {Result(Receipt("ROUTE_ENDED"));return;}
            if (watchedRoutes.TryGetValue(plan.id, out var old) && old.state.reservation == "reserved") { Result(Receipt("ROUTE_ALREADY_RESERVED")); return; }
            if (old != null && old.state.reservation == "releaseFailed") { Result(Receipt(old.state.reason ?? "SIGNALS_RELEASE_FAILED")); return; }
            // A watch/advisory route may describe an occupied approach; the
            // occupancy check belongs to a real reservation. Switch alignment
            // is still verified for both modes, while normal/protected modes
            // remain blocked by live rolling stock.
            string error = RouteAlignment(plan);
            if (error == null && mode != "none") error = RouteOccupancy(plan, LiveRouteCars());
            if (error != null) { Result(Receipt(error)); return; }
            if (mode != "none")
            {
                var path = plan.tracks.Concat(plan.reservationTracks).Concat(plan.turntables.Select(s => tables[s.id].def.track)).Distinct().ToArray();
                error = routeLocks.Acquire(plan.id, mode, path, plan.switches);
                if (error != null) { Result(Receipt(error)); return; }
                try
                {
                    if (signals != null && signals.Ready) { error = signals.ReserveRoute(plan, multiplayer.PublishSignalReservation, out var acquiredSignals); plan.reservedSignals=acquiredSignals; }
                    else if (plan.reservedSignals.Length > 0) error = "CAPABILITY_UNAVAILABLE";
                    error = error ?? RouteAlignment(plan);
                    if (error != null) { signals?.ReleaseRoute(plan.id, multiplayer.PublishSignalReservation); routeLocks.Release(plan.id); Result(Receipt(error)); return; }
                }
                catch { try { signals?.ReleaseRoute(plan.id, multiplayer.PublishSignalReservation); } finally { routeLocks.Release(plan.id); } throw; }
            }
            pendingSwitches.Remove(plan.id);
            var item = new WatchedRoute { plan = plan, state = new RouteRuntimeState { id=plan.id,mode=mode,reservation=mode=="none"?"none":"reserved",time=Protocol.Now,signals=plan.reservedSignals } };
            watchedRoutes[plan.id] = item;
            PruneWatchedRoutes(); SendRouteStates(); Result(Receipt());
        }
        private string ReleaseWatchedRoute(WatchedRoute route, string lifecycle, string reason)
        {
            // Release only our native reservations. The adapter tracks replacement/clear events.
            string error=null;
            try { error=signals!=null?signals.ReleaseRoute(route.plan.id, multiplayer.PublishSignalReservation):(route.cleanupRequired||route.plan.reservedSignals.Length>0)&&(route.state.reservation=="reserved"||route.state.reservation=="releaseFailed")?"CAPABILITY_UNAVAILABLE":null; }
            catch(Exception e) {error="SIGNALS_RELEASE_FAILED";Main.Log(error,e);}
            finally { routeLocks.Release(route.plan.id); }
            route.state = new RouteRuntimeState { id=route.plan.id,mode=route.state.mode,lifecycle=lifecycle,
                reservation=error!=null?"releaseFailed":route.state.reservation=="none"?"none":"released",reason=error??reason,time=Protocol.Now };
            route.cleanupRequired=error!=null;
            routesDirty=true;
            return error;
        }
        private void ResetRoutes(string reason)
        {
            foreach (var route in watchedRoutes.Values.Where(r=>r.state.lifecycle=="active").ToArray()) ReleaseWatchedRoute(route,"interrupted",reason);
            routeLocks.Clear(); routesDirty=true;
        }
        private void PruneWatchedRoutes()
        {
            foreach(var route in watchedRoutes.Values.Where(r=>r.state.lifecycle!="active").OrderByDescending(r=>r.state.time).Skip(200).ToArray()) watchedRoutes.Remove(route.plan.id);
        }
        private void PollRoutes()
        {
            if (watchedRoutes.Count==0) return;
            if (Main.Bridge?.Connected!=true) { ResetRoutes("GAME_DISCONNECTED"); return; }
            if (!multiplayer.Authority || Main.Config.ReadOnly) { ResetRoutes(!multiplayer.Authority?"HOST_REQUIRED":"FORBIDDEN"); return; }
            if(Time.realtimeSinceStartup<nextRouteCheck)return;
            nextRouteCheck=Time.realtimeSinceStartup+.25f;
            var live=LiveRouteCars(); var byId=live.ToDictionary(c=>c.id);
            foreach(var r in watchedRoutes.Values.Where(x=>x.state.lifecycle=="active").ToArray())
            {
                var plan=r.plan;
                if(plan.trainCars.Length>0)
                {
                    if(plan.trainCars.Any(id=>!byId.TryGetValue(id,out var car)||car.consist!=plan.train||car.derailed) || live.Any(car=>car.consist==plan.train&&!plan.trainCars.Contains(car.id)))
                    { ReleaseWatchedRoute(r,"interrupted","TRAIN_CHANGED"); continue; }
                    // Completion is based on every car's two bogies, never just the leading locomotive.
                    if(plan.from!=plan.to && plan.trainCars.All(id=>byId[id].track1==plan.to&&byId[id].track2==plan.to))
                    { ReleaseWatchedRoute(r,"completed",null); continue; }
                }
                if(r.state.reservation!="reserved")continue;
                string error=RouteAlignment(plan);
                if(error==null && plan.reservedSignals.Length>0 && (signals==null||!signals.RouteReservationsIntact(plan.id)))error="RESERVATION_LOST";
                if(error!=null)ReleaseWatchedRoute(r,"active",error);
            }
            if(routesDirty)SendRouteStates();
        }
        private RouteRuntimeState[] RouteStates() => watchedRoutes.Values.Select(r=>r.state).ToArray();
        private void SendRouteStates()
        {
            routesDirty=false;
            Main.Bridge?.Send(new WireFrame {kind="state",batch=new GameBatch {epoch=epoch,topologyRevision=topologyRevision,routeStates=RouteStates()}});
        }
    }
}
