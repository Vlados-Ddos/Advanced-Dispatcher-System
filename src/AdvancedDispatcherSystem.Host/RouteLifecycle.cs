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
        plan.reservationState=plan.reservationState=="releaseFailed"?"releaseFailed":plan.reservationState=="none"?"none":"released";plan.reservedSignals=[];plan.warnings=[];
        Log("route"+char.ToUpperInvariant(state[0])+state[1..],plan.owner,plan.id,reason??"","route",source:"dispatcher");
    }
    private void ApplyRouteStates(RouteRuntimeState[] states,bool reset)
    {
        if(states==null)return;
        foreach(var state in states)
        {
            if(!routes.TryGetValue(state.id,out var r))continue;
            if(Ended(r)) {
                // Cleanup can finish after cancellation; it must not reactivate the route.
                if(r.reservationState=="releaseFailed"&&state.lifecycle==r.lifecycle) {
                    r.reservationState=state.reservation;r.reservedSignals=state.signals;r.reason=state.reason;
                }
                continue;
            }
            r.reservationMode=state.mode;r.reservationState=state.reservation;r.reservedSignals=state.signals;r.reason=state.reason;
            if(state.lifecycle!="active")EndRoute(r,state.lifecycle,state.reason);
            else if(r.lifecycle!="preparing")r.lifecycle="active";
        }
        if(reset)foreach(var r in routes.Values.Where(r=>!Ended(r)&&r.lifecycle!="preparing"&&states.All(s=>s.id!=r.id)))
            EndRoute(r,"interrupted","RESERVATION_LOST");
    }
    public RoutePlan Route(string id)
    {
        lock(sync)return routes.TryGetValue(id??"",out var r)?Json.Read<RoutePlan>(Json.Bytes(r)):null;
    }
    public void RouteOperation(string id,string state,string reason=null)
    {
        lock(sync)if(routes.TryGetValue(id??"",out var r)&&!Ended(r)) {r.lifecycle=state;r.reason=reason;UpdateRoutes(true);}
    }
    public string RouteBlocker(string id,bool reserve=false,bool alignmentOnly=false)
    {
        lock(sync)
        {
            if(!routes.TryGetValue(id??"",out var r))return "NOT_FOUND";
            if(Ended(r))return "ROUTE_ENDED";
            Evaluate(r);
            if(reserve&&r.reservationState=="reserved")return "ROUTE_ALREADY_RESERVED";
            var allowed=new HashSet<string> {"SWITCH_MISALIGNED","TURNTABLE_MISALIGNED","SIGNAL_STOP","SIGNAL_OFF"};
            if (alignmentOnly)
                foreach (var code in new[]{"TRACK_OCCUPIED","BLOCK_OCCUPIED","OPPOSING_TRAIN"}) allowed.Add(code);
            // BLOCK_OCCUPIED without a concrete train is a stale/advisory
            // signal-block state. Native RouteOccupancy remains authoritative
            // for actual cars at registration time.
            if (r.conflicts.Any(c=>c.code=="BLOCK_OCCUPIED"&&string.IsNullOrEmpty(c.train))) allowed.Add("BLOCK_OCCUPIED");
            return r.conflicts.FirstOrDefault(c=>!allowed.Contains(c.code))?.code;
        }
    }
    public Command RouteRegistration(string id,string actor,string mode="none")
    {
        lock(sync)
        {
            if(!routes.TryGetValue(id??"",out var r)||Ended(r))return null;
            Evaluate(r);
            var copy=Json.Read<RoutePlan>(Json.Bytes(r));
            var ids=copy.itinerary.Where(p=>p.kind=="signals").Select(p=>p.id).Distinct().ToArray();
            copy.reservedSignals=ids.Where(id=>signals.TryGetValue(id,out var s)&&s.reservable&&s.parent==null)
                .GroupBy(id=>signals[id].block!=null&&blocks.TryGetValue(signals[id].block,out var block)?string.Join("|",block.tracks.Select((t,i)=>t+":"+(i<block.directions.Length?block.directions[i]:0))):id)
                .Select(g=>g.OrderBy(id=>signals[id].shuntingSignal).First()).ToArray();
            copy.reservationTracks=copy.tracks.Concat(copy.reservedSignals.Where(id=>signals[id].block!=null && blocks.ContainsKey(signals[id].block)).SelectMany(id=>blocks[signals[id].block].tracks.Concat(blocks[signals[id].block].extraTracks))).Distinct().ToArray();
            copy.history=[];copy.conflicts=[];copy.itinerary=[];
            if(mode=="none")copy.reservedSignals=[];
            return new Command {id=Guid.NewGuid().ToString("N"),kind=mode=="none"?"watchRoute":"reserveGameRoute",target=id,route=copy,
                reservationMode=mode,actor=actor,epoch=Topology.epoch,topologyRevision=Topology.revision};
        }
    }
}
