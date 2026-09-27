using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private readonly Dictionary<string, List<BlockState>> trackBlocks = new();
    private readonly Dictionary<string, List<SignalState>> trackSignals = new();
    private byte[] lastRoutes;
    private long nextRouteUpdate;
    private void IndexRouteObjects()
    {
        trackBlocks.Clear(); trackSignals.Clear();
        foreach (var b in blocks.Values) foreach (var id in b.tracks.Concat(b.extraTracks).Distinct())
        { if (!trackBlocks.TryGetValue(id, out var list)) trackBlocks[id] = list = new(); list.Add(b); }
        foreach (var s in signals.Values) if (s.objectKind != "sign" && s.track != null)
        { if (!trackSignals.TryGetValue(s.track, out var list)) trackSignals[s.track] = list = new(); list.Add(s); }
    }
    private CarState? RouteTrain(string train)
    {
        if (string.IsNullOrEmpty(train)) return null;
        if (cars.TryGetValue(train, out var car)) return car;
        CarState? first = null;
        foreach (var c in cars.Values) if (c.consist == train) { if (c.locomotive) return c; if (!first.HasValue) first = c; }
        return first;
    }
    private RoutePlan BuildPlan(string from, string to, string train, string[] via = null)
    {
        var car = RouteTrain(train);
        if (!string.IsNullOrEmpty(train) && !car.HasValue) return null;
        var plan = Graph?.FindRouteVia(car?.track1 ?? from, to, via ?? new string[0], car?.direction ?? 0, car?.span1);
        if (plan != null) {plan.train = car?.consist;plan.trainCar=car?.id;plan.trainCars=car.HasValue?cars.Values.Where(c=>c.consist==car.Value.consist).Select(c=>c.id).ToArray():[];}
        return plan;
    }
    public RoutePlan Preview(string from, string to, string train = null, string[] via = null)
    {
        lock (sync) { var plan = BuildPlan(from, to, train, via); if (plan != null) Evaluate(plan); return plan; }
    }
    public string RouteFailure(string from,string to,string train=null, string[] via=null) {
        lock(sync) {
            var car=RouteTrain(train);
            if(!string.IsNullOrEmpty(train)&&!car.HasValue)return "TRAIN_UNAVAILABLE";
            if (Graph == null) return "WORLD_NOT_READY";
            if (via != null && via.Length > 0) {
                var start = car?.track1 ?? from;
                if (via.Length > 128 || via.Any(string.IsNullOrEmpty) || via.Any(id => !Graph.Tracks.ContainsKey(id)) || via.Distinct(StringComparer.Ordinal).Count() != via.Length || via.Contains(start) || via.Contains(to)) return "NO_ROUTE";
                return Graph.FindRouteVia(start, to, via, car?.direction ?? 0, car?.span1) == null ? "NO_ROUTE" : null;
            }
            return Graph.RouteFailure(car?.track1 ?? from,to,car?.direction ?? 0);
        }
    }
    public RoutePlan Plan(string from, string to, string actor, string train = null, bool preparing = false, string[] via = null)
    {
        lock (sync)
        {
            if (routes.Values.Count(r=>!Ended(r)) >= 100) return null;
            var plan = BuildPlan(from, to, train, via); if (plan == null) return null;
            plan.lifecycle=preparing?"preparing":"active"; plan.id = Guid.NewGuid().ToString("N"); plan.owner = actor; plan.createdAt = Protocol.Now;
            plan.name = (RouteTrain(train)?.name ?? Graph.Tracks[plan.from].name) + " → " + Graph.Tracks[plan.to].name;
            routes.Add(plan.id, plan); UpdateRoutes(true); Log("routePlanned", actor, plan.id, plan.name, "route", source: "dispatcher"); return plan;
        }
    }
    public bool CancelRoute(string id, string actor)
    {
        lock (sync)
        {
            if (id == null || !routes.TryGetValue(id,out var plan)) return false;
            EndRoute(plan,"cancelled");UpdateRoutes(true); return true;
        }
    }
    public (RoutePlan Plan, Command[] Commands) RouteCommands(string id, string actor)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var plan) || Ended(plan)) return default;
            Evaluate(plan);
            var commands = new List<Command>();
            foreach (var step in plan.turntables)
                if (!TurntableRules.Aligned(turntables.GetValueOrDefault(step.id), step) || Protocol.Now-(turntables.GetValueOrDefault(step.id)?.sampledAt ?? 0)>5000)
                    commands.Add(new Command { id = Guid.NewGuid().ToString("N"), routeId=plan.id, kind = "setTurntable", epoch = Topology.epoch, topologyRevision = Topology.revision, expectedRevision = turntables.GetValueOrDefault(step.id)?.revision ?? -1, target = step.id, from = step.from, to = step.to, fromEnd = step.fromEnd, toEnd = step.toEnd, index = step.position, actor = actor });
            foreach (var step in plan.switches)
                if (switches.TryGetValue(step.id, out var sw) && sw.branch != step.branch)
                    commands.Add(new Command { id = Guid.NewGuid().ToString("N"), routeId=plan.id, kind = "setSwitch", epoch = Topology.epoch, topologyRevision = Topology.revision, expectedRevision = sw.revision, target = step.id, branch = step.branch, actor = actor });
            return (Json.Read<RoutePlan>(Json.Bytes(plan)), commands.ToArray());
        }
    }
    private void UpdateRoutes(bool immediate = false)
    {
        if (!immediate && (routes.Count == 0 || Environment.TickCount64 < nextRouteUpdate)) return;
        nextRouteUpdate = Environment.TickCount64 + 500;
        foreach (var r in routes.Values) Evaluate(r);
        foreach(var r in routes.Values.Where(Ended).OrderByDescending(r=>r.endedAt).Skip(200).ToArray())routes.Remove(r.id);
        var values = routes.Values.ToArray(); var bytes = Json.Bytes(values);
        if (lastRoutes != null && bytes.AsSpan().SequenceEqual(lastRoutes)) return;
        lastRoutes = bytes; Publish("routes", values);
    }
    private void Evaluate(RoutePlan plan)
    {
        if(Ended(plan))return;
        var conflicts = new List<RouteConflict>(); var warnings = new HashSet<string>(); var dedup = new HashSet<string>();
        void Warn(string code, string kind, string target, string track = null, string train = null)
        {
            warnings.Add(code);
            if (conflicts.Count < 200 && dedup.Add(code + ":" + target + ":" + train)) conflicts.Add(new RouteConflict { code = code, kind = kind, target = target, track = track, train = train });
        }
        bool routeValid = plan.tracks != null && plan.directions != null && plan.tracks.Length == plan.directions.Length && plan.tracks.Length > 0;
        if (routeValid) for (int n = 0; n < plan.tracks.Length; n++) {
            if (!Graph.Tracks.ContainsKey(plan.tracks[n]) || Math.Abs(plan.directions[n]) != 1) { routeValid = false; break; }
            if (n + 1 < plan.tracks.Length && !Graph.RouteLinks(plan.tracks[n],plan.directions[n]>0?1:0)
                .Any(l=>l.track==plan.tracks[n+1] && (l.end==0?1:-1)==plan.directions[n+1])) {routeValid=false;break;}
        }
        if (!routeValid) {
            Warn("TOPOLOGY_CHANGED","routes",plan.id);
            plan.itinerary=[];plan.remaining=0;ResolveConflicts(plan,conflicts.ToArray());
            plan.warnings=warnings.ToArray();plan.status="warning";return;
        }
        var itinerary = new List<RoutePoint>(); var seenBlocks = new HashSet<string>();
        var car = RouteTrain(plan.trainCar ?? plan.train); int startIndex = 0; double startSpan = plan.startSpan, distance = 0;
        void TableWarnings(TurntableStep step) {
            var state=turntables.GetValueOrDefault(step.id);string bridge=Graph.Turntables[step.id].track;
            if (!TurntableRules.Aligned(state,step)) Warn("TURNTABLE_MISALIGNED","turntables",step.id,bridge);
            if(state==null || Protocol.Now-state.sampledAt>5000) Warn("TURNTABLE_UNKNOWN","turntables",step.id,bridge);
            else if(state.moving) Warn("TURNTABLE_BUSY","turntables",step.id,bridge);
            else if(!TurntableRules.Aligned(state,step)&&!state.available&&state.reason!=null) Warn(state.reason,"turntables",step.id,bridge);
            bool own=false,other=false;
            if(trackCars.TryGetValue(bridge,out var bridgeCars)) foreach(var member in bridgeCars) if(cars.TryGetValue(member,out var occupant)) {
                if(plan.train!=null && occupant.consist==plan.train) own=true;
                else { other=true;Warn("TRACK_OCCUPIED","tracks",bridge,bridge,occupant.consist); }
            }
            if(!occupancy.TryGetValue(bridge,out var bridgeOccupancy)||Protocol.Now-bridgeOccupancy.sampledAt>5000) Warn("OCCUPANCY_UNKNOWN","tracks",bridge);
            else if(bridgeOccupancy.occupied&&!own&&!other) Warn("TRACK_OCCUPIED","tracks",bridge);
        }
        if (car.HasValue)
        {
            startIndex = Array.IndexOf(plan.tracks, car.Value.track1);
            var crossing = startIndex<0 ? plan.turntables.FirstOrDefault(step=>Graph.Turntables[step.id].track==car.Value.track1) : null;
            if (crossing != null) {
                startIndex=Array.IndexOf(plan.tracks,crossing.to);
                startSpan=plan.directions[startIndex]>0 ? 0 : Graph.Tracks[crossing.to].length;
                var state=turntables.GetValueOrDefault(crossing.id);TableWarnings(crossing);
                int bridgeDirection=state?.front==crossing.to?-1:state?.rear==crossing.to?1:0;
                if(bridgeDirection!=0 && car.Value.direction!=0 && car.Value.direction!=bridgeDirection) Warn("TRAIN_OPPOSITE_ROUTE","trains",plan.train);
                var bridge=Graph.Tracks[car.Value.track1];
                distance=Math.Max(0,Math.Min(bridge.length,bridgeDirection<0?car.Value.span1:bridge.length-car.Value.span1));
                itinerary.Add(new RoutePoint {kind="turntables",id=crossing.id,track=bridge.id,current=true,distance=0});
            }
            else if (startIndex < 0) { Warn("TRAIN_OFF_ROUTE", "trains", plan.train); startIndex = 0; }
            else startSpan = car.Value.span1;
            if (crossing==null && car.Value.direction != 0 && car.Value.direction != plan.directions[startIndex]) Warn("TRAIN_OPPOSITE_ROUTE", "trains", plan.train);
        }
        else if (plan.train != null) Warn("TRAIN_UNAVAILABLE", "trains", plan.train);
        for (int i = startIndex; i < plan.tracks.Length; i++)
        {
            string id = plan.tracks[i]; int direction = plan.directions[i];
            if (!Graph.Tracks.TryGetValue(id, out var track)) { Warn("TOPOLOGY_CHANGED", "tracks", id); continue; }
            double origin = i == startIndex ? startSpan : direction > 0 ? 0 : track.length;
            bool ownTrainPresent = false, otherTrainPresent = false;
            if (trackCars.TryGetValue(id, out var members)) foreach (string member in members)
            {
                if (!cars.TryGetValue(member, out var train)) continue;
                if (plan.train != null && train.consist == plan.train) { ownTrainPresent = true; continue; }
                otherTrainPresent = true;
                Warn(train.direction != 0 && train.direction != direction ? "OPPOSING_TRAIN" : "TRACK_OCCUPIED", "tracks", id, id, train.consist);
            }
            if (!occupancy.TryGetValue(id, out var o) || Protocol.Now - o.sampledAt > 5000) Warn("OCCUPANCY_UNKNOWN", "tracks", id);
            else if (o.occupied && !otherTrainPresent && !ownTrainPresent) Warn("TRACK_OCCUPIED", "tracks", id);
            foreach (var other in routes.Values) if (other != plan && Claims(other) && Array.IndexOf(other.tracks, id) >= 0) Warn("ROUTE_OVERLAP", "routes", other.id, id, other.train);
            if (trackBlocks.TryGetValue(id, out var onTrack)) foreach (var block in onTrack)
            {
                if (block.reserved && !plan.reservedSignals.Contains(block.signal)) Warn("SIGNAL_RESERVED", "blocks", block.id, id);
                if (block.occupied && !block.trains.Any() && !ownTrainPresent) Warn("BLOCK_OCCUPIED", "blocks", block.id, id);
                int at = Array.IndexOf(block.tracks, id);
                if (at < 0) continue; // Crossing/extra occupancy is a conflict, not a traversed block.
                if (at >= 0 && at < block.directions.Length && block.directions[at] != 0 && block.directions[at] != direction) continue;
                // Multiple signal views of the same physical track set share one itinerary entry.
                string key = string.Join("|", block.tracks.OrderBy(x => x, StringComparer.Ordinal));
                if (seenBlocks.Add(key)) itinerary.Add(new RoutePoint { kind = "blocks", id = block.id, track = id, distance = distance, current = i == startIndex });
            }
            if (trackSignals.TryGetValue(id, out var onSignals)) foreach (var s in onSignals)
            {
                if (!routeValid || !RouteSignalRules.Applies(s, plan, i, origin)) continue;
                itinerary.Add(new RoutePoint { kind = "signals", id = s.id, track = id, distance = distance + Math.Abs(s.span - origin) });
                if (s.stop || s.off) Warn(s.off ? "SIGNAL_OFF" : "SIGNAL_STOP", "signals", s.id, id);
            }
            distance += Math.Max(0, direction > 0 ? track.length - origin : origin);
            if (i + 1 < plan.tracks.Length)
            {
                var link = Graph.RouteLinks(id, direction > 0 ? 1 : 0).FirstOrDefault(l => l.track == plan.tracks[i + 1] && (l.end == 0 ? 1 : -1) == plan.directions[i + 1]);
                if (link == null) Warn("TOPOLOGY_CHANGED", "tracks", id);
                else if (link.turntable != null)
                {
                    itinerary.Add(new RoutePoint { kind = "turntables", id = link.turntable, track = id, distance = distance });
                    var step = plan.turntables.First(s => s.id == link.turntable);
                    TableWarnings(step);
                    distance += link.crossingLength;
                }
                else if (link.junction != null)
                {
                    itinerary.Add(new RoutePoint { kind = "switches", id = link.junction, track = id, distance = distance });
                    if (!switches.TryGetValue(link.junction, out var sw) || sw.branch != link.branch) Warn("SWITCH_MISALIGNED", "switches", link.junction, id);
                    foreach (var other in routes.Values) if (other != plan && Claims(other) && other.switches.Any(s => s.id == link.junction && s.branch != link.branch)) Warn("SWITCH_PLAN_CONFLICT", "routes", other.id, id);
                }
            }
        }
        itinerary.Add(new RoutePoint { kind = "tracks", id = plan.to, track = plan.to, distance = distance });
        plan.remaining = Math.Round(distance, 1); plan.itinerary = itinerary.OrderBy(p => p.distance).Take(2048).ToArray();
        // A native rejection is the summary of an existing concrete conflict, not a second warning.
        if (plan.reason != null && plan.reason != "SWITCH_MISALIGNED" && plan.reason != "TURNTABLE_MISALIGNED" && !warnings.Contains(plan.reason)) Warn(plan.reason, "routes", plan.id);
        ResolveConflicts(plan,conflicts.ToArray()); plan.warnings = warnings.ToArray(); plan.status = plan.lifecycle=="preparing"?"preparing":warnings.Count > 0 ? "warning" : plan.reservationState=="reserved"?"reserved":"aligned";
    }
}
