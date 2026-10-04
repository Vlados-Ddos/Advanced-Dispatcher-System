using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private static bool Ended(RoutePlan route) => route.endedAt != 0;
    private static bool Claims(RoutePlan route) => !Ended(route) && (route.lifecycle != "failed" || route.reservationState=="reserved");
    private void ResolveConflicts(RoutePlan plan, RouteConflict[] current)
    {
        long now=Protocol.Now;
        var old=plan.conflicts.ToDictionary(c=>c.id??(c.code+":"+c.target+":"+c.track+":"+c.train));
        var live=new HashSet<string>();
        foreach(var c in current)
        {
            c.id=c.code+":"+c.target+":"+c.track+":"+c.train;c.route=plan.id;live.Add(c.id);
            c.firstSeen=old.TryGetValue(c.id,out var before)?before.firstSeen:now;
            c.lastSeen=before?.lastSeen ?? now;
            if(plan.id!=null && before==null)Log("routeWarning",plan.owner,plan.id,c.code+" · "+c.target,"route","warning","dispatcher");
        }
        var resolved=plan.conflicts.Where(c=>!live.Contains(c.id??(c.code+":"+c.target+":"+c.track+":"+c.train))).ToArray();
        foreach(var c in resolved) {c.resolvedAt=now;if(plan.id!=null)Log("routeWarningResolved",plan.owner,plan.id,c.code,"route","info","dispatcher");}
        if(resolved.Length>0)plan.history=plan.history.Concat(resolved).TakeLast(100).ToArray();
        plan.conflicts=current;
    }
    private void EndRoute(RoutePlan plan,string state,string reason=null)
    {
        if(Ended(plan))return;
        ResolveConflicts(plan,Array.Empty<RouteConflict>());
        plan.lifecycle=plan.status=state;plan.reason=reason;plan.endedAt=Protocol.Now;
        plan.editPreview=null;plan.recalculationState="none";plan.canRecalculate=false;plan.invalidationReason=null;
        plan.reservationState=plan.reservationState=="releaseFailed"?"releaseFailed":plan.reservationState=="none"?"none":"released";plan.reservedSignals=[];plan.warnings=[];
        Log("route"+char.ToUpperInvariant(state[0])+state[1..],plan.owner,plan.id,reason??"","route",source:"dispatcher");
    }
    private static bool IsRouteInvalidationReason(string reason) => reason=="TRACK_OCCUPIED"||reason=="PROTECTION_OCCUPIED"||reason=="OPPOSING_TRAIN"||reason=="SIGNAL_PATH_CHANGED"||reason=="SIGNAL_PROTECTION_CHANGED"||reason=="RESERVATION_LOST"||reason=="SWITCH_MISALIGNED"||reason=="ROUTE_OVERLAP"||reason=="TOPOLOGY_CHANGED"||reason=="SIGNAL_RESERVED"||reason=="SWITCH_PLAN_CONFLICT"||reason=="SWITCH_LOCKED"||reason=="TURNTABLE_MISALIGNED";
    private void ApplyRouteStates(RouteRuntimeState[] states,bool reset)
    {
        if(states==null)return;
        foreach(var state in states)
        {
            if(!routes.TryGetValue(state.id,out var r)) {
                // A lost/damaged Host store must not hide a route still owned
                // by the game. Recover only an authoritative complete plan.
                if(!reset || state.lifecycle!="active" || !ValidRoutePath(state.plan))continue;
                r=Json.Read<RoutePlan>(Json.Bytes(state.plan));
                r.editPreview=null;r.recalculationState="none";
                r.conflicts=[];r.history=[];r.warnings=[];
                routes.Add(r.id,r);
            }
            if (state.plan != null && state.plan.id == r.id && state.plan.tracks?.Length > 0)
            {
                r.train = state.plan.train; r.trainCar = state.plan.trainCar;
                r.trainCars = state.plan.trainCars ?? Array.Empty<string>();
                r.tracks = state.plan.tracks; r.directions = state.plan.directions; r.switches = state.plan.switches; r.turntables = state.plan.turntables;
                r.itinerary = state.plan.itinerary; r.reservationTracks = state.plan.reservationTracks; r.reservedSignals = state.plan.reservedSignals;
                r.releasedSignals = state.plan.releasedSignals ?? Array.Empty<string>(); r.releasedTracks = state.plan.releasedTracks ?? Array.Empty<string>();
                r.length = state.plan.length; r.startSpan = state.plan.startSpan; r.remaining = state.plan.remaining;
                r.via=state.plan.via;r.manualVia=state.plan.manualVia;r.jobId=state.plan.jobId;r.taskIndex=state.plan.taskIndex;r.taskId=state.plan.taskId;
                r.staged=state.plan.staged; r.stageStatus=state.plan.stageStatus; r.stagedReason=state.plan.stagedReason;
                r.stagedReservationMode=state.plan.stagedReservationMode; r.stageIndex=state.plan.stageIndex;
                r.activeFrom=state.plan.activeFrom; r.activeTo=state.plan.activeTo; r.stages=state.plan.stages ?? Array.Empty<RouteStage>();
                r.editVersion = state.plan.editVersion;
                if(!string.IsNullOrEmpty(state.plan.editVersion) && (r.recalculationState=="replacing" || r.recalculationState=="unconfirmed") && r.editPreview?.id==state.plan.editVersion) {
                    r.recalculationState="none";r.editPreview=null;r.reason=null;r.recalculationAttempts=0;
                }
                else if(reset && (r.recalculationState=="unconfirmed" || r.recalculationState=="replacing")) {
                    // Full replay confirms the old version: the timed-out edit
                    // was not applied. Keep the failure visible and permit retry.
                    r.recalculationState="failed";r.editPreview=null;r.reason="GAME_DISCONNECTED";
                }
                else if(reset && r.recalculationState=="recalculating") {
                    r.recalculationState="none";r.recalculationKey=null;
                }
            }
            if (state.lifecycle == "stageCompleted" && r.staged && !Ended(r))
            {
                if (!AdvanceCompletedStage(r))
                {
                    r.reason = "STAGED_ROUTE_COMPLETED";
                    EndRoute(r, "completed", r.reason);
                }
                continue;
            }
            if(Ended(r)) {
                // Cleanup can finish after cancellation; it must not reactivate the route.
                if(r.reservationState=="releaseFailed"&&state.lifecycle==r.lifecycle) {
                    r.reservationState=state.reservation;r.reservedSignals=state.signals;r.reason=state.reason;
                }
                continue;
            }
            r.reservationMode=state.mode;r.reservationState=state.reservation;r.reservedSignals=state.signals;r.nativeReason=state.reason;
            if(r.recalculationState=="none")r.reason=state.reason;
            if(state.lifecycle!="active")EndRoute(r,state.lifecycle,state.reason);
            else if(r.lifecycle!="preparing")r.lifecycle="active";
        }
        if(reset)foreach(var r in routes.Values.Where(r=>!Ended(r)&&r.lifecycle!="preparing"&&states.All(s=>s.id!=r.id)))
            EndRoute(r,"interrupted","RESERVATION_LOST");
        if(reset) persistedAwaitingReplay=false;
    }

    private bool AdvanceCompletedStage(RoutePlan route)
    {
        if (!route.staged || route.stages == null || route.stageIndex + 1 >= route.stages.Length) return false;
        var next = route.stages[route.stageIndex + 1];
        var previous = route.stages[route.stageIndex];
        previous.status = "completed"; previous.reservationState = route.reservationState;
        route.stageIndex++;
        next.status = "available"; next.reservationState = "none";
        route.stageStatus = "available";
        route.activeFrom = next.from; route.activeTo = next.to;
        route.tracks = next.tracks.ToArray(); route.directions = next.directions.ToArray();
        route.switches = next.switches.ToArray(); route.turntables = next.turntables.ToArray();
        route.length = next.length; route.remaining = next.remaining; route.startSpan = next.startSpan;
        route.reservationTracks = Array.Empty<string>(); route.reservedSignals = Array.Empty<string>();
        route.releasedTracks=Array.Empty<string>();route.releasedSignals=Array.Empty<string>();
        route.reservationState = "none"; route.reservationMode = "none"; route.lifecycle = route.status = "stageAvailable";
        route.reason = "STAGED_NEXT_LEG_AVAILABLE"; route.canRecalculate = false; route.invalidationReason = null;
        Log("routeStageCompleted", route.owner, route.id, previous.from + " -> " + previous.to, "route", "info", "dispatcher");
        Log("routeStageAvailable", route.owner, route.id, next.from + " -> " + next.to, "route", "info", "dispatcher");
        UpdateRoutes(true); return true;
    }

    public RoutePlan PrepareNextStage(string id, string mode = null)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var route) || Ended(route) || !route.staged || route.stageStatus != "available") return null;
            route.stageStatus = "active"; route.lifecycle = route.status = "preparing";
            route.reservationMode = mode == "normal" || mode == "protected" ? mode : route.stagedReservationMode ?? "normal";
            route.reason = null; route.reservationState = "none";
            UpdateRoutes(true); return Json.Read<RoutePlan>(Json.Bytes(route));
        }
    }
    public RoutePlan Route(string id)
    {
        lock(sync)return routes.TryGetValue(id??"",out var r)?Json.Read<RoutePlan>(Json.Bytes(r)):null;
    }
    public void RouteOperation(string id,string state,string reason=null,string mode=null)
    {
        lock(sync)if(routes.TryGetValue(id??"",out var r)&&!Ended(r)) {r.lifecycle=state;r.reason=reason;if(mode!=null && r.reservationState!="reserved"){r.reservationMode=mode;if(r.staged&&mode!="none")r.stagedReservationMode=mode;}UpdateRoutes(true);}
    }
    private void RefreshRecalculationState(RoutePlan route)
    {
        route.canRecalculate=false;
        if(Ended(route)||route.lifecycle=="preparing"||route.reservationState=="releaseFailed"||
            route.recalculationState=="replacing"||route.recalculationState=="unconfirmed")return;
        if(route.staged&&route.stageStatus=="available") { route.invalidationReason=null; route.canRecalculate=false; return; }
        route.invalidationReason=route.conflicts.FirstOrDefault(c=>IsRouteInvalidationReason(c.code))?.code;
        if(route.invalidationReason==null) {
            if(route.recalculationState!="none" && route.editPreview?.reason!="ROUTE_POINTS_CHANGED") {
                route.editPreview=null;route.recalculationState="none";route.reason=null;route.recalculationAttempts=0;
            }
            return;
        }
        if(route.recalculationState=="none")route.recalculationState="available";
        route.canRecalculate=route.recalculationState!="preview"&&route.recalculationState!="recalculating";
    }
    public string RouteBlocker(string id,bool reserve=false,bool alignmentOnly=false,bool forceReservation=false)
    {
        lock(sync)
        {
            if(!routes.TryGetValue(id??"",out var r))return "NOT_FOUND";
            if(Ended(r))return "ROUTE_ENDED";
            var jobError=JobRouteError(r.jobId,r.taskIndex,r.taskId,RouteTrain(r.trainCar??r.train)?.track1??r.from,r.to,r.via,true,r.trainCar??r.train);
            if(jobError!=null)return jobError;
            Evaluate(r);
            if(reserve&&r.reservationState=="reserved")return "ROUTE_ALREADY_RESERVED";
            var allowed=new HashSet<string> {"SWITCH_MISALIGNED","TURNTABLE_MISALIGNED","SIGNAL_STOP","SIGNAL_OFF"};
            if (alignmentOnly)
                foreach (var code in new[]{"TRACK_OCCUPIED","PROTECTION_OCCUPIED","BLOCK_OCCUPIED","OPPOSING_TRAIN"}) allowed.Add(code);
            // BLOCK_OCCUPIED without a concrete train is a stale/advisory
            // signal-block state. Native RouteOccupancy remains authoritative
            // for actual cars at registration time.
            if (r.conflicts.Any(c=>c.code=="BLOCK_OCCUPIED"&&string.IsNullOrEmpty(c.train))) allowed.Add("BLOCK_OCCUPIED");
            return r.conflicts.FirstOrDefault(c=>!allowed.Contains(c.code) &&
                !(forceReservation && r.reservationMode=="normal" && c.code=="ROUTE_OVERLAP" &&
                    routes.TryGetValue(c.target ?? "", out var other) && other.reservationMode!="protected"))?.code;
        }
    }
    public Command RouteRegistration(string id,string actor,string mode="none")
    {
        lock(sync)
        {
            if(!routes.TryGetValue(id??"",out var r)||Ended(r))return null;
            Evaluate(r);
            var copy=Json.Read<RoutePlan>(Json.Bytes(r));
            PopulateRouteReservations(copy, mode);
            copy.history=[];copy.conflicts=[];copy.itinerary=[];
            return new Command {id=Guid.NewGuid().ToString("N"),kind=mode=="none"?"watchRoute":"reserveGameRoute",target=id,route=copy,
                reservationMode=mode,actor=actor,epoch=Topology.epoch,topologyRevision=Topology.revision};
        }
    }
    private void PopulateRouteReservations(RoutePlan copy, string mode)
    {
            var ids=copy.itinerary.Where(p=>p.kind=="signals"&&!p.boundary).Select(p=>p.id).Distinct().ToArray();
            // Route warnings and map presentation include every applicable head,
            // including shunting heads. Native route reservation is different:
            // TrackReserver reserves the controller's main route block, while a
            // shunting head is an auxiliary movement head and must not become an
            // independent requirement for an ordinary route. SignalState.shuntingSignal
            // is captured from DV Signals' Signal.IsShunting/controller classification.
            copy.reservedSignals=ids.Where(id=>signals.TryGetValue(id,out var s)&&s.reservable&&s.parent==null&&!s.shuntingSignal&&!string.Equals(s.type,"Shunting",StringComparison.OrdinalIgnoreCase))
                .GroupBy(id=>signals[id].block!=null&&blocks.TryGetValue(signals[id].block,out var block)?NativeBlockKey(block):id)
                .Select(g=>g.First()).ToArray();
            copy.reservationTracks=copy.tracks.Concat(copy.reservedSignals.Where(id=>signals[id].block!=null && blocks.ContainsKey(signals[id].block)).SelectMany(id=>blocks[signals[id].block].tracks.Concat(blocks[signals[id].block].extraTracks))).Distinct().ToArray();
            if(mode=="none")copy.reservedSignals=[];
    }
    private static string NativeBlockKey(BlockState block)
    {
        if(block==null)return "";
        return SignalBlockIdentity.Key(block.tracks,block.directions,block.extraTracks);
    }
}
