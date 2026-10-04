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
        private sealed class WatchedRoute {
            public RoutePlan plan; public RouteRuntimeState state; public bool cleanupRequired;
            public readonly HashSet<string> occupiedTracks = new HashSet<string>(StringComparer.Ordinal);
            public bool destinationEntered;
        }
        private readonly Dictionary<string, WatchedRoute> watchedRoutes = new Dictionary<string, WatchedRoute>();
        // Original turnout positions for a route that is still being prepared.
        // They allow a rejected/unknown registration to undo only our own
        // partial alignment without touching an active route's switches.
        private readonly Dictionary<string, Dictionary<string, byte>> pendingSwitches = new Dictionary<string, Dictionary<string, byte>>();
        private readonly RouteLocks routeLocks = new RouteLocks();
        private TrackGraph executionGraph;
        private float nextRouteCheck;
        private bool routesDirty;
        private string editingRoute;
        private readonly Dictionary<string,string[]> routeBodyFootprints = new Dictionary<string,string[]>();
        public static bool ProtectedSwitchAllowed(Junction junction, int branch) => Main.Runtime == null || Main.Runtime.AllowsProtectedSwitch(junction, branch);
        // MP packet handlers use the same decision as the native switch entry point.
        public bool AllowsProtectedSwitch(Junction junction, int branch)
        {
            if (!world || Main.Config.ReadOnly || !multiplayer.Authority || junction == null) return true;
            return !junctionIds.TryGetValue(junction, out var id) || routeLocks.AllowsSwitch(id, branch);
        }
        private bool AllowsProtectedSwitch(Junction junction, int branch, string requester)
        {
            if (!world || Main.Config.ReadOnly || !multiplayer.Authority || junction == null) return true;
            return !junctionIds.TryGetValue(junction, out var id) || routeLocks.AllowsSwitch(id, branch, requester);
        }
        [HarmonyPatch(typeof(Junction), "Switch", new[] { typeof(Junction.SwitchMode), typeof(byte) })]
        private static class ProtectedJunction
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Junction __instance, byte branch) => Main.Runtime == null || Main.Runtime.AllowsProtectedSwitch(__instance, __instance.outBranches.Count == 0 ? branch : branch % __instance.outBranches.Count, Main.Runtime.editingRoute);
        }
        private CarState[] LiveRouteCars(IEnumerable<string> extraTrackedCars = null)
        {
            routeBodyFootprints.Clear();
            var trackedCars = new HashSet<string>(watchedRoutes.Values.Where(r=>r.state.lifecycle=="active")
                .SelectMany(r=>r.plan.trainCars ?? Array.Empty<string>()),StringComparer.Ordinal);
            if (extraTrackedCars != null)
                foreach (var id in extraTrackedCars)
                    if (!string.IsNullOrEmpty(id)) trackedCars.Add(id);
            // Capture newly coupled members in the same snapshot as the
            // expected CarGUID anchors. Waiting for the next poll would omit
            // the new locomotive's physical tail from the first release check.
            var trackedSets = new HashSet<int>(carList.Where(entry => entry.car != null && trackedCars.Contains(entry.id) && entry.car.trainset != null)
                .Select(entry => entry.car.trainset.id));
            var result = new List<CarState>(carList.Count);
            foreach (var entry in carList)
            {
                var car = entry.car; if (car == null) continue;
                var p = car.transform.TransformPoint(car.Bounds.center) - WorldMover.currentMove;
                var state = new CarState { id = entry.id, consist = car.trainset == null ? "car:" + entry.id : "train:" + car.trainset.id,
                    track1 = TrackId(car.FrontBogie?.track), track2 = TrackId(car.RearBogie?.track), span1 = car.FrontBogie?.traveller?.Span ?? 0,
                    span2 = car.RearBogie?.traveller?.Span ?? 0, length = car.InterCouplerDistance, speed = car.GetForwardSpeed() * 3.6,
                    x = p.x, z = p.z, sampledAt = Protocol.Now, derailed = car.FrontBogie?.HasDerailed == true || car.RearBogie?.HasDerailed == true };
                result.Add(state);
                if((trackedCars.Contains(entry.id) || car.trainset != null && trackedSets.Contains(car.trainset.id)) && executionGraph!=null && car.FrontBogie!=null && car.RearBogie!=null) {
                    var front=car.transform.InverseTransformPoint(car.FrontBogie.transform.position).z;
                    var rear=car.transform.InverseTransformPoint(car.RearBogie.transform.position).z;
                    var half=Math.Max(car.Bounds.size.z,car.InterCouplerDistance)/2d;
                    routeBodyFootprints[entry.id]=executionGraph.Footprint(state,
                        Math.Max(0,car.Bounds.center.z+half-front),Math.Max(0,rear-car.Bounds.center.z+half));
                }
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
            var released = new HashSet<string>(plan.releasedTracks ?? Array.Empty<string>());
            var movement = new HashSet<string>((plan.tracks ?? Array.Empty<string>()).Where(t => !released.Contains(t)));
            foreach (var step in plan.turntables) if (tables.TryGetValue(step.id, out var table)) movement.Add(table.def.track);
            // A physically passed track may still be part of an occupied
            // native block's protection. Keep checking it for foreign cars.
            var path = new HashSet<string>((plan.reservationTracks ?? Array.Empty<string>()).Concat(movement));
            var own = new HashSet<string>(plan.trainCars);
            foreach (var c in observed)
            {
                var footprint = executionGraph?.Footprint(c) ?? new[] { c.track1, c.track2 };
                // Car IDs can be recreated by Persistent Jobs and by native
                // consist edits. The stable consist identity is authoritative
                // for occupancy ownership; trainCars remains the expected
                // membership used for lifecycle validation below.
                bool belongsToTrain = own.Contains(c.id) || c.consist == plan.train;
                if (footprint.Any(t => t != null && path.Contains(t)) && !belongsToTrain)
                    return footprint.Any(t=>t!=null&&movement.Contains(t))?"TRACK_OCCUPIED":"PROTECTION_OCCUPIED";
                if (belongsToTrain && (c.consist != plan.train || c.derailed)) return "TRAIN_CHANGED";
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
                SetJunctionBranch(junction,entry.Value); RefreshJunction(junction);
                if (junction.selectedBranch != entry.Value) error ??= "SWITCH_ROLLBACK_FAILED";
            }
            pendingSwitches.Remove(routeId ?? "");
            return error;
        }
        private bool TryRouteCommand(Command c)
        {
            switch(c.kind) {
                case "watchRoute": case "reserveGameRoute": case "replaceGameRoute": case "releaseGameRoute": case "endGameRoute":
                    RouteCommand(c);return true;
                default:return false;
            }
        }
        private void RouteCommand(Command c)
        {
            CommandResult Receipt(string code = null) {
                if(code=="SIGNAL_ROUTE_MISMATCH"||code=="SIGNAL_PATH_CHANGED"||code=="SIGNAL_PROTECTION_CHANGED"||code=="TOPOLOGY_CHANGED")
                    UnityEngine.Debug.LogWarning("ADS ROUTE_NATIVE_REJECTED route="+c.target+" code="+code+" topology="+epoch+":"+topologyRevision+" mode="+c.reservationMode);
                return new CommandResult { id=c.id,target=c.target,status=code==null?"applied":"rejected",code=code };
            }
            if (c.kind == "replaceGameRoute") { ReplaceRouteCommand(c, Receipt); return; }
            if (c.kind == "releaseGameRoute" || c.kind == "endGameRoute")
            {
                if(c.kind=="endGameRoute")foreach(var table in tables.Values.Where(t=>t.command?.routeId==c.target).ToArray())FinishTable(table,"ROUTE_CANCELLED");
                string releaseError=null;
                if (c.action == "failed" || c.action == "cancelled") releaseError = RollbackRouteSwitches(c.target);
                if (watchedRoutes.TryGetValue(c.target ?? "", out var existing)) {
                    var nativeError=ReleaseWatchedRoute(existing, c.kind == "endGameRoute" ? c.action == "completed" ? "completed" : "cancelled" : existing.state.lifecycle, c.reason);
                    releaseError ??= nativeError;
                }
                else if(!string.IsNullOrEmpty(c.target)) {
                    // A reserve can fail before registration while its rollback still owns
                    // native signals. Explicit cleanup must reach that ownership as well.
                    releaseError ??= signals?.ReleaseRoute(c.target,multiplayer.PublishSignalReservation);
                    string lifecycle=c.kind=="endGameRoute"?(c.action=="completed"?"completed":"cancelled"):
                        (c.action=="cancelled"||c.action=="completed"||c.action=="interrupted"||c.action=="failed"?c.action:"active");
                    watchedRoutes[c.target]=new WatchedRoute {plan=new RoutePlan{id=c.target},cleanupRequired=releaseError!=null,
                        state=new RouteRuntimeState{id=c.target,lifecycle=lifecycle,reservation=releaseError==null?"released":"releaseFailed",reason=releaseError??c.reason,time=Protocol.Now}};
                }
                PruneWatchedRoutes();
                SendRouteStates(); Result(Receipt(releaseError)); return;
            }
            var plan = c.route;
            if (plan == null || string.IsNullOrEmpty(plan.id) || plan.id != c.target || plan.tracks == null || plan.tracks.Length == 0 || plan.tracks.Length > 4096 || plan.switches == null || plan.turntables == null || executionGraph == null) { Result(Receipt("INVALID_ROUTE")); return; }
            string mode = c.kind == "watchRoute" ? "none" : c.reservationMode;
            plan.reservationMode=mode;
            var orderError=RouteOrderState(plan);if(orderError!=null){Result(Receipt(orderError));return;}
            // The preview's footprint may describe the pre-alignment block.
            // Start with the user path; native resolution replaces this field
            // before the full occupancy/ownership validation callback.
            plan.reservationTracks=plan.tracks.ToArray();
            if (mode != "none" && mode != "normal" && mode != "protected") { Result(Receipt("INVALID_RESERVATION")); return; }
            if (mode == "protected" && !multiplayer.ProtectedSwitches) { Result(Receipt("PROTECTION_UNAVAILABLE")); return; }
            if(mode!="none"&&((signals!=null&&!signals.Ready)||(signals==null&&signalsError!=null))) {Result(Receipt("CAPABILITY_UNAVAILABLE"));return;}
            if(watchedRoutes.TryGetValue(plan.id,out var ended)&&ended.state.lifecycle!="active" &&
                !(ended.state.lifecycle=="stageCompleted" && ended.plan.staged && plan.staged &&
                  plan.stageIndex==ended.plan.stageIndex+1 && plan.activeFrom==ended.plan.activeTo &&
                  plan.jobId==ended.plan.jobId && plan.train==ended.plan.train)) {Result(Receipt("ROUTE_ENDED"));return;}
            if (watchedRoutes.TryGetValue(plan.id, out var old) && old.state.reservation == "reserved") { Result(Receipt("ROUTE_ALREADY_RESERVED")); return; }
            if (old != null && old.state.reservation == "releaseFailed") { Result(Receipt(old.state.reason ?? "SIGNALS_RELEASE_FAILED")); return; }
            // A new acquisition must validate the entire path it is about to
            // claim. Previous release history belongs to the old acquisition,
            // and cannot exempt those rails from its occupancy checks.
            plan.releasedTracks=Array.Empty<string>();plan.releasedSignals=Array.Empty<string>();
            // A watch/advisory route may describe an occupied approach; the
            // occupancy check belongs to a real reservation. Switch alignment
            // is still verified for both modes, while normal/protected modes
            // remain blocked by live rolling stock.
            string error = RouteAlignment(plan);
            if (error == null && mode != "none") error = RouteOccupancy(plan, LiveRouteCars(plan.trainCars));
            if (error != null) { Result(Receipt(error)); return; }
            if (mode != "none")
            {
                // Reserve the user path provisionally. Native resolution adds
                // its fresh protection footprint in the validation callback;
                // stale preview flanks must not claim a neighbour's route.
                var path = plan.tracks.Concat(plan.turntables.Select(s => tables[s.id].def.track)).Distinct().ToArray();
                error = routeLocks.Acquire(plan.id, mode, path, plan.switches, plan);
                if (error != null) { Result(Receipt(error)); return; }
                try
                {
                    if (signals != null && signals.Ready) {
                        error = signals.ReserveRoute(plan, multiplayer.PublishSignalReservation, out var acquiredSignals, () =>
                            RouteOccupancy(plan,LiveRouteCars(plan.trainCars)) ?? routeLocks.Replace(plan.id,mode,plan.reservationTracks.Concat(plan.turntables.Select(s=>tables[s.id].def.track)).Distinct().ToArray(),plan.switches,plan));
                        plan.reservedSignals=acquiredSignals;
                    }
                    else if (plan.reservedSignals.Length > 0) error = "CAPABILITY_UNAVAILABLE";
                    error = error ?? RouteAlignment(plan);
                    if (error != null) {
                        var cleanup=signals?.ReleaseRoute(plan.id, multiplayer.PublishSignalReservation);routeLocks.Release(plan.id);
                        if(cleanup!=null) {
                            watchedRoutes[plan.id]=new WatchedRoute {plan=plan,cleanupRequired=true,state=new RouteRuntimeState {id=plan.id,mode=mode,reservation="releaseFailed",reason=cleanup,time=Protocol.Now}};
                            SendRouteStates();
                        }
                        Result(Receipt(cleanup??error)); return;
                    }
                }
                catch { try { signals?.ReleaseRoute(plan.id, multiplayer.PublishSignalReservation); } finally { routeLocks.Release(plan.id); } throw; }
            }
            pendingSwitches.Remove(plan.id);
            if (mode != "none" && signals != null && plan.trainCars?.Length > 0)
                signals.SeedRouteOccupancy(plan.id, plan.trainCars
                    .SelectMany(id => routeBodyFootprints.TryGetValue(id, out var footprint)
                        ? footprint ?? Array.Empty<string>()
                        : Array.Empty<string>()));
            var item = new WatchedRoute { plan = plan, state = new RouteRuntimeState { id=plan.id,mode=mode,reservation=mode=="none"?"none":"reserved",time=Protocol.Now,signals=plan.reservedSignals } };
            if (mode == "none") LiveRouteCars(plan.trainCars);
            SeedPhysicalRouteProgress(item);
            watchedRoutes[plan.id] = item;
            PruneWatchedRoutes(); SendRouteStates(); Result(Receipt());
        }
        private void ReplaceRouteCommand(Command c, Func<string, CommandResult> receipt)
        {
            if (c.route == null || string.IsNullOrEmpty(c.target) || c.route.id != c.target || executionGraph == null) { Result(receipt("INVALID_ROUTE")); return; }
            if (!watchedRoutes.TryGetValue(c.target, out var current) || current.state.lifecycle != "active" || current.state.reservation == "releaseFailed") { Result(receipt("ROUTE_NOT_RESERVED")); return; }
            var next = c.route;
            var orderError=RouteOrderState(next);if(orderError!=null){Result(receipt(orderError));return;}
            if(next.tracks==null||next.tracks.Length==0||next.switches==null||next.turntables==null) {Result(receipt("INVALID_ROUTE"));return;}
            next.reservationTracks=next.tracks.ToArray();
            next.releasedTracks=Array.Empty<string>();next.releasedSignals=Array.Empty<string>();
            if (next.reservationMode != "normal" && next.reservationMode != "protected" && next.reservationMode != "none") { Result(receipt("INVALID_RESERVATION")); return; }
            if (next.reservationMode == "protected" && !multiplayer.ProtectedSwitches) { Result(receipt("PROTECTION_UNAVAILABLE")); return; }
            if(next.reservationMode!="none"&&((signals!=null&&!signals.Ready)||(signals==null&&signalsError!=null))) {Result(receipt("CAPABILITY_UNAVAILABLE"));return;}
            string error = RouteOccupancy(next, LiveRouteCars(next.trainCars));
            if (error != null) { Result(receipt(error)); return; }
            var changed = new List<(Junction junction, byte branch)>();
            bool RestoreSwitches()
            {
                bool restored=true;
                foreach (var item in changed.AsEnumerable().Reverse())
                {
                    try { if (item.junction.selectedBranch != item.branch && AllowsProtectedSwitch(item.junction, item.branch, c.target) && SwitchSafety(item.junction) == null) SetJunctionBranch(item.junction, item.branch); }
                    catch(Exception e) { Main.Log("SWITCH_ROLLBACK_FAILED",e); }
                    RefreshJunction(item.junction);
                    if(item.junction.selectedBranch!=item.branch)restored=false;
                }
                return restored;
            }
            editingRoute=c.target;
            try
            {
                var path = next.tracks.Concat((next.turntables ?? Array.Empty<TurntableStep>()).Select(s => tables[s.id].def.track)).Distinct().ToArray();
                if(next.reservationMode!="none")error=routeLocks.CanReplace(c.target,next.reservationMode,path,next.switches,next);
                if(error!=null){Result(receipt(error));return;}
                foreach (var step in next.switches ?? Array.Empty<RouteStep>())
                {
                    if (!junctionById.TryGetValue(step.id ?? "", out var junction) || junction == null || step.branch < 0 || step.branch >= junction.outBranches.Count) { error = "NOT_FOUND"; break; }
                    if (junction.selectedBranch == step.branch) continue;
                    if (!AllowsProtectedSwitch(junction, step.branch, c.target)) { error = "SWITCH_LOCKED"; break; }
                    error = SwitchSafety(junction); if (error != null) break;
                    changed.Add((junction, junction.selectedBranch));
                    SetJunctionBranch(junction, (byte)step.branch); RefreshJunction(junction);
                    if (junction.selectedBranch != step.branch) { error = "SWITCH_CHANGED_EXTERNALLY"; break; }
                }
                if (error == null) error = RouteAlignment(next);
                if (error != null) { RestoreSwitches(); Result(receipt(error)); return; }
                if (next.reservationMode!="none" && signals != null && signals.Ready) {
                    error = signals.ReplaceRoute(next, multiplayer.PublishSignalReservation, out var acquired, RestoreSwitches, () =>
                        RouteOccupancy(next,LiveRouteCars(next.trainCars)) ?? routeLocks.CanReplace(c.target,next.reservationMode,next.reservationTracks.Concat(next.turntables.Select(s=>tables[s.id].def.track)).Distinct().ToArray(),next.switches,next));
                    if(error==null)next.reservedSignals=acquired;
                }
                else if ((next.reservedSignals ?? Array.Empty<string>()).Length > 0) error = "CAPABILITY_UNAVAILABLE";
                if (error != null) {
                    RestoreSwitches();
                    if(error=="RESERVATION_ROLLBACK_FAILED") {current.state.reservation="releaseFailed";current.state.reason=error;current.cleanupRequired=true;routeLocks.Release(c.target);SendRouteStates();}
                    Result(receipt(error)); return;
                }
                path=next.reservationTracks.Concat(next.tracks).Concat(next.turntables.Select(s=>tables[s.id].def.track)).Distinct().ToArray();
                error = next.reservationMode=="none"?null:routeLocks.Replace(c.target, next.reservationMode, path, next.switches ?? Array.Empty<RouteStep>(), next);
                if (error != null)
                {
                    try { if (signals != null) signals.ReplaceRoute(current.plan, multiplayer.PublishSignalReservation, out _, RestoreSwitches); } catch(Exception e) { Main.Log("RESERVATION_ROLLBACK_FAILED",e); }
                    RestoreSwitches(); Result(receipt(error)); return;
                }
                next.reservedSignals = next.reservedSignals ?? Array.Empty<string>();
                if (next.reservationMode != "none" && signals != null && next.trainCars?.Length > 0)
                    signals.SeedRouteOccupancy(next.id, next.trainCars
                        .SelectMany(id => routeBodyFootprints.TryGetValue(id, out var footprint)
                            ? footprint ?? Array.Empty<string>()
                            : Array.Empty<string>()));
                current.plan = next;
                current.occupiedTracks.Clear();
                SeedPhysicalRouteProgress(current);
                current.state = new RouteRuntimeState { id = next.id, mode = next.reservationMode, reservation = next.reservationMode=="none"?"none":"reserved", signals = next.reservedSignals, time = Protocol.Now };
                current.cleanupRequired = false; pendingSwitches.Remove(next.id); routesDirty = true; SendRouteStates();
                var success=receipt(null);success.route=next;Result(success);
            }
            catch (Exception e)
            {
                RestoreSwitches(); Main.Log("ROUTE_REPLACEMENT_FAILED", e); Result(receipt("ROUTE_REPLACEMENT_FAILED"));
            }
            finally { editingRoute=null; }
        }
        private static bool SetJunctionBranch(Junction junction, byte branch)
        {
            junction.Switch(Junction.SwitchMode.FORCED,branch);
            return junction.selectedBranch==branch;
        }
        private void RefreshJunction(Junction junction)
        {
            ReadSwitch(junction);
            if(junctionIds.TryGetValue(junction,out var id)&&switchStates.TryGetValue(id,out var state))
                Main.Bridge?.Send(new WireFrame {kind="state",batch=new GameBatch {epoch=epoch,topologyRevision=topologyRevision,switches=new[]{state}}});
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
            foreach (var route in watchedRoutes.Values.Where(r=>r.state.lifecycle=="active").ToArray()) ReleaseWatchedRoute(route,reason=="TOPOLOGY_CHANGED"?"active":"interrupted",reason);
            routeLocks.Clear(); routesDirty=true;
        }
        private void ResetRouteTopology()
        {
            // Release geometry-dependent ownership, but keep route identity so
            // the rebuilt graph can accept a confirmed edit of the same route.
            ResetRoutes("TOPOLOGY_CHANGED"); executionGraph=null;
        }
        private void PruneWatchedRoutes()
        {
            foreach(var route in watchedRoutes.Values.Where(r=>r.state.lifecycle!="active").OrderByDescending(r=>r.state.time).Skip(200).ToArray()) watchedRoutes.Remove(route.plan.id);
        }
        private void SeedPhysicalRouteProgress(WatchedRoute route)
        {
            foreach (var id in route.plan.trainCars ?? Array.Empty<string>())
                if (routeBodyFootprints.TryGetValue(id, out var footprint) && footprint != null)
                    route.occupiedTracks.UnionWith(footprint);
        }
        private void UpdatePhysicalRouteProgress(WatchedRoute route, HashSet<string> occupied)
        {
            var plan = route.plan;
            var ordered = plan.tracks ?? Array.Empty<string>();
            var prior = plan.releasedTracks ?? Array.Empty<string>();
            // Progress is physical tail clearance, independently of the native
            // signal block boundaries. Keep repeats until their last occurrence
            // is behind every car. Re-evaluating the prior set also removes a
            // false "passed" state immediately when the train rolls back.
            var passed = ordered.Distinct(StringComparer.Ordinal)
                .Where(track => route.occupiedTracks.Contains(track) &&
                    RouteProgressRules.IsBehindCurrent(ordered, new[] { track }, occupied)).ToArray();
            route.occupiedTracks.UnionWith(occupied);
            if (!prior.SequenceEqual(passed))
            {
                plan.releasedTracks = passed;
                route.state.time = Protocol.Now; routesDirty = true;
            }
        }
        private bool DestinationCleared(WatchedRoute route, IEnumerable<CarState> live)
        {
            var destination = route.plan.activeTo ?? route.plan.to;
            if (string.IsNullOrEmpty(destination)) return false;
            var members = new HashSet<string>(route.plan.trainCars ?? Array.Empty<string>(), StringComparer.Ordinal);
            var body = live.Where(car => members.Contains(car.id) && car.consist == route.plan.train)
                .SelectMany(car => routeBodyFootprints.TryGetValue(car.id, out var footprint) ? footprint ?? Array.Empty<string>() : Array.Empty<string>())
                .Where(track => !string.IsNullOrEmpty(track)).ToArray();
            if (body.Contains(destination, StringComparer.Ordinal)) { route.destinationEntered = true; return false; }
            if (!route.destinationEntered || body.Length == 0 || executionGraph == null || route.plan.directions == null || route.plan.directions.Length == 0)
                return false;
            // Leaving the destination backwards onto an earlier route rail is
            // a turnback/rollback, not route completion. Require an observed
            // body track connected to the destination's forward exit mouth.
            if (!executionGraph.Tracks.TryGetValue(destination, out var destinationTrack)) return false;
            var exitLinks = route.plan.directions[route.plan.directions.Length - 1] > 0 ? destinationTrack.b : destinationTrack.a;
            var routeTracks = new HashSet<string>(route.plan.tracks ?? Array.Empty<string>(), StringComparer.Ordinal);
            return body.Any(track => !routeTracks.Contains(track) && exitLinks.Any(link => link != null && link.track == track));
        }
        private void ReleaseClearedRouteClaims(WatchedRoute route, string[] nativeReleasedTracks)
        {
            var plan = route.plan;
            var nativeProtection = plan.reservedSignals.Length > 0 ? signals?.RouteProtectionTracks(plan.id) : Array.Empty<string>();
            if (nativeProtection == null) return; // Unknown native coverage cannot authorize a local unlock.
            var protectedTracks = new HashSet<string>(nativeProtection, StringComparer.Ordinal);
            var releasable = (plan.releasedTracks ?? Array.Empty<string>()).Concat(nativeReleasedTracks ?? Array.Empty<string>())
                .Where(track => !protectedTracks.Contains(track)).Distinct(StringComparer.Ordinal).ToArray();
            // The local track lock models movement ownership and may be
            // released as soon as the tail clears. Native signal protection
            // remains authoritative through reservationTracks/AllTracks.
            var physicallyCleared = (plan.releasedTracks ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            if (physicallyCleared.Length > 0) routeLocks.ReleaseTracks(plan.id, physicallyCleared);
            var remaining = (plan.reservationTracks ?? Array.Empty<string>()).Except(releasable, StringComparer.Ordinal).ToArray();
            if (!remaining.SequenceEqual(plan.reservationTracks ?? Array.Empty<string>()))
            {
                plan.reservationTracks = remaining;
                routeLocks.ReleaseTracks(plan.id, releasable);
                route.state.time = Protocol.Now; routesDirty = true;
            }
            var remainingSet = new HashSet<string>(remaining, StringComparer.Ordinal);
            var freedSwitches = plan.switches.Where(s => junctionById.TryGetValue(s.id, out var junction) && junction != null &&
                !remainingSet.Contains(TrackId(junction.inBranch?.track)) &&
                !junction.outBranches.Any(b => remainingSet.Contains(TrackId(b?.track)))).Select(s => s.id).ToArray();
            if (freedSwitches.Length > 0)
            {
                routeLocks.ReleaseSwitches(plan.id, freedSwitches);
                plan.switches = plan.switches.Where(s => !freedSwitches.Contains(s.id)).ToArray();
                routesDirty = true;
            }
            if (route.state.reservation == "reserved" &&
                (plan.reservationTracks ?? Array.Empty<string>()).Length == 0 &&
                (plan.reservedSignals ?? Array.Empty<string>()).Length == 0)
            {
                route.state.reservation = "released";
                routesDirty = true;
            }
        }
        private void PollRoutes()
        {
            if (watchedRoutes.Count==0) return;
            // Web Host is a presentation/command client. Its restart does not
            // end native routes; Replay sends their authoritative state back.
            if (Main.Bridge?.Connected!=true) return;
            if (!multiplayer.Authority || Main.Config.ReadOnly) { ResetRoutes(!multiplayer.Authority?"HOST_REQUIRED":"FORBIDDEN"); return; }
            if(Time.realtimeSinceStartup<nextRouteCheck)return;
            nextRouteCheck=Time.realtimeSinceStartup+.25f;
            var live=LiveRouteCars(); var byId=live.ToDictionary(c=>c.id);
            foreach(var r in watchedRoutes.Values.Where(x=>x.state.lifecycle=="active").ToArray())
            {
                var plan=r.plan;
                // LiveRouteCars() traverses every TrainCar and reads bogie
                // transforms.  The poll already captured an authoritative
                // snapshot above; recapturing it once per active route made
                // route lifecycle polling O(activeRoutes * consistSize) and
                // could consume the Unity frame budget with many routes.
                // Reuse that same-frame snapshot for stage/completion checks.
                bool suspended = RouteConsistRules.IsTemporarilySuspended(plan.train, plan.trainCars, live, carStates);
                if(plan.trainCars.Length>0)
                {
                    // Coupling/re-coupling can recreate the native Trainset
                    // object and assign a new consist id even though every
                    // route car is still physically present. Treat that as a
                    // membership refresh, not as a route invalidation. A real
                    // split/removal remains rejected when an expected member
                    // is missing, derailed, or no longer shares one consist.
                    var expectedIds = new HashSet<string>(plan.trainCars.Where(id=>!string.IsNullOrEmpty(id)),StringComparer.Ordinal);
                    var expectedCars = expectedIds.Select(id=>byId.TryGetValue(id,out var car)?car:(CarState?)null)
                        .Where(car=>car.HasValue).Select(car=>car.Value).ToArray();
                    var observedConsists = expectedCars.Select(car=>car.consist).Where(consist=>!string.IsNullOrEmpty(consist))
                        .Distinct(StringComparer.Ordinal).ToArray();
                    bool completeExpected = expectedCars.Length==expectedIds.Count && expectedCars.All(car=>!car.derailed) && observedConsists.Length==1;
                    if (completeExpected)
                    {
                        // The authoritative live snapshot includes any newly
                        // coupled cars in the same native trainset. Adopt the
                        // new identity and members before occupancy/release
                        // checks so the added locomotive is tracked for tail
                        // release and cannot look like a foreign obstruction.
                        var observedConsist = observedConsists[0];
                        var adoptedIds = live.Where(car=>car.consist==observedConsist && !string.IsNullOrEmpty(car.id))
                            .Select(car=>car.id).Distinct(StringComparer.Ordinal).ToArray();
                        if (plan.train != observedConsist || !expectedIds.SetEquals(adoptedIds))
                        {
                            plan.train = observedConsist;
                            plan.trainCars = expectedIds.Concat(adoptedIds).Distinct(StringComparer.Ordinal).ToArray();
                            r.state.time = Protocol.Now; routesDirty = true;
                        }
                    }
                    bool changed = !completeExpected || plan.trainCars.Any(id=>!byId.TryGetValue(id,out var car)||car.consist!=plan.train||car.derailed);
                    // During Persistent Jobs Suspend every TrainCar can be
                    // absent from the runtime for a short lifecycle gap. The
                    // stable GUID and explicit suspended snapshot are the only
                    // evidence accepted here; never guess a replacement ID.
                    if(changed && !suspended)
                    { ReleaseWatchedRoute(r,"interrupted","TRAIN_CHANGED"); continue; }
                    if(suspended) continue;
                    // Native signal-block reservations follow the entire
                    // consist. A block is releasable only after no bogie/body
                    // footprint of any route car still intersects it. This
                    // runs before obstruction polling so released geometry is
                    // no longer treated as part of the active reservation.
                    if (plan.trainCars.All(id=>routeBodyFootprints.TryGetValue(id,out var body)&&body!=null&&body.Length>0))
                    {
                        var occupied = new HashSet<string>();
                        foreach (var car in live.Where(c => c.consist == plan.train && plan.trainCars.Contains(c.id)))
                        {
                            var footprint = routeBodyFootprints[car.id];
                            foreach (var track in footprint ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(track)) occupied.Add(track);
                        }
                        UpdatePhysicalRouteProgress(r, occupied);
                        string[] releasedSignals = Array.Empty<string>(), releasedTracks = Array.Empty<string>();
                        var releaseError = r.state.reservation == "reserved" && signals != null && plan.reservedSignals.Length > 0
                            ? signals.ReleaseCompletedSegments(plan.id, plan.reservedSignals, occupied.ToArray(),
                                plan.tracks ?? Array.Empty<string>(), plan.directions != null && plan.directions.Length > 0 && plan.directions[0] < 0,
                                multiplayer.PublishSignalReservation, out releasedSignals, out releasedTracks) : null;
                        if (releaseError != null)
                        {
                            if (r.state.reason != releaseError) { r.state.reason = releaseError; routesDirty = true; }
                        }
                        else if (releasedSignals.Length > 0)
                        {
                            plan.reservedSignals = plan.reservedSignals.Except(releasedSignals, StringComparer.Ordinal).ToArray();
                            plan.releasedSignals = (plan.releasedSignals ?? Array.Empty<string>()).Concat(releasedSignals).Distinct(StringComparer.Ordinal).ToArray();
                            r.state.signals = plan.reservedSignals.ToArray();
                            if (plan.reservedSignals.Length == 0 &&
                                !(plan.reservationTracks ?? Array.Empty<string>()).Any())
                                r.state.reservation = "released";
                            r.state.time = Protocol.Now;
                            routesDirty = true;
                        }
                        if (releaseError == null && r.state.reservation == "reserved") ReleaseClearedRouteClaims(r, releasedTracks);
                    }
                    // A newly coupled locomotive must participate in stage
                    // completion too, so test only after membership refresh.
                    if (plan.staged && plan.stageIndex + 1 < (plan.stages?.Length ?? 0) && ReachedStage(plan, live))
                    { ReleaseWatchedRoute(r, "stageCompleted", "STAGED_NEXT_LEG_AVAILABLE"); continue; }
                    if(RouteTaskCompleted(plan) && ReachedStage(plan,live)) {ReleaseWatchedRoute(r,"completed",null);continue;}
                    // Completion is based on every car's two bogies, never just the leading locomotive.
                    var destination = plan.activeTo ?? plan.to;
                    if (plan.activeFrom != destination && ReachedStage(plan,live)) r.destinationEntered = true;
                    if (plan.activeFrom != destination && DestinationCleared(r, live))
                    { 
                        // The final native block is released only after the
                        // complete body footprint has left the destination.
                        // ReleaseWatchedRoute then clears native protection in
                        // one authoritative operation, without an early
                        // destination-arrival release.
                        plan.releasedTracks = (plan.releasedTracks ?? Array.Empty<string>())
                            .Concat(plan.tracks ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
                        ReleaseWatchedRoute(r,"completed",null); continue;
                    }
                }
                if(r.state.reservation!="reserved")continue;
                // A consist can appear on a reserved path after the native
                // reservation was accepted (station/production spawns and
                // multiplayer changes are the common cases). Keep the
                // reservation ownership intact so the dispatcher can inspect
                // and release it deliberately, but publish an explicit
                // obstruction reason instead of claiming that the route is
                // still clear. The host's route evaluator will add the
                // concrete track/train conflict to the visible route record.
                string obstruction=RouteOccupancy(plan,live);
                if(obstruction!=null)
                {
                    if(r.state.reason!=obstruction){r.state.reason=obstruction;routesDirty=true;}
                }
                else if(r.state.reason=="TRACK_OCCUPIED"||r.state.reason=="PROTECTION_OCCUPIED"||r.state.reason=="ROUTE_OBSTRUCTED")
                {
                    r.state.reason=null;routesDirty=true;
                }
                string error=RouteAlignment(plan);
                if(error==null && plan.reservedSignals.Length>0)error=signals==null?"RESERVATION_LOST":signals.RouteReservationIssue(plan.id);
                if(error!=null)
                {
                    // A signal/block drift is recoverable: retain the watched
                    // route and its switch ownership long enough for the Host
                    // to create a same-ID edit preview. Only a confirmed native
                    // loss is shown as released; cancellation/completion still
                    // uses ReleaseWatchedRoute above.
                    if(error=="RESERVATION_LOST") { routeLocks.Release(plan.id); r.state.reservation="released"; r.state.reason=error; routesDirty=true; }
                    else ReleaseWatchedRoute(r,"active",error);
                }
            }
            if(routesDirty)SendRouteStates();
        }
        private bool ReachedStage(RoutePlan plan, CarState[] live)
        {
            var destination = plan.activeTo ?? plan.to;
            if (string.IsNullOrEmpty(destination) || plan.trainCars == null || plan.trainCars.Length == 0) return false;
            var byId = live.ToDictionary(c => c.id);
            return plan.trainCars.All(id => byId.TryGetValue(id, out var car) && car.track1 == destination && car.track2 == destination &&
                routeBodyFootprints.TryGetValue(id,out var body)&&body!=null&&body.Length>0&&body.All(track=>track==destination));
        }
        private RouteRuntimeState[] RouteStates() => watchedRoutes.Values.Select(r =>
        {
            // Serialization runs on the IPC worker. Never hand it the mutable
            // state that PollRoutes updates on Unity's thread.
            return new RouteRuntimeState { id=r.state.id,lifecycle=r.state.lifecycle,mode=r.state.mode,
                reservation=r.state.reservation,reason=r.state.reason,time=r.state.time,
                signals=r.state.signals,plan=r.plan.Snapshot() };
        }).ToArray();
        private void SendRouteStates()
        {
            routesDirty=false;
            Main.Bridge?.Send(new WireFrame {kind="state",batch=new GameBatch {epoch=epoch,topologyRevision=topologyRevision,routeStates=RouteStates()}});
        }
    }
}
