using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AdvancedDispatcherSystem.Core
{
    public sealed class RouteSearchLimitException : Exception
    {
        public RouteSearchLimitException() : base("ROUTE_SEARCH_LIMIT") { }
    }
    public sealed partial class TrackGraph
    {
        public RoutePlan FindRoute(string from, string to, int direction = 0, double? startSpan = null,
            Func<string,double> dynamicPenalty = null, CancellationToken cancellation = default)
            => FindRouteVia(from,to,Array.Empty<string>(),direction,startSpan,dynamicPenalty,cancellation);

        public RoutePlan FindRouteVia(string from, string to, string[] via, int direction = 0, double? startSpan = null,
            Func<string,double> dynamicPenalty = null, CancellationToken cancellation = default)
        {
            via = via ?? Array.Empty<string>();
            if(via.Length>128 || from==null || to==null || direction < -1 || direction > 1 ||
                startSpan.HasValue && !Finite(startSpan.Value))return null;
            var points=new[]{from}.Concat(via).Concat(new[]{to}).ToArray();
            if(points.Any(id=>string.IsNullOrEmpty(id)||!Tracks.ContainsKey(id)) ||
                via.Length>0 && points.Distinct(StringComparer.Ordinal).Count()!=points.Length)return null;
            // Fast unconstrained shortest walk is a lower bound. If it already
            // satisfies the reservation constraints, no more costly search is needed.
            var quick=FindViaUnconstrained(from,to,via,direction,startSpan,dynamicPenalty,cancellation);
            if(quick!=null && quick.tracks.Distinct(StringComparer.Ordinal).Count()==quick.tracks.Length)return quick;
            if(!HasOrderedWalk(from,to,via,direction,cancellation))return null;
            return FindConstrained(from,to,via,direction,startSpan,dynamicPenalty,cancellation);
        }

        /// <summary>
        /// Passenger jobs may legitimately traverse the same physical rail
        /// more than once (for example a platform approach followed by a
        /// turnback and an opposite-direction departure).  A continuous
        /// reservation still cannot contain that footprint twice, so this
        /// method uses the existing staged planner after trying the normal
        /// unique-rail route.  It is intentionally separate from
        /// FindRouteVia: ordinary dispatcher routes keep the strict
        /// unique-rail contract and existing callers cannot accidentally
        /// request a cyclic reservation.
        /// </summary>
        public RoutePlan FindPassengerRouteVia(string from, string to, string[] via, int direction = 0,
            double? startSpan = null, Func<string,double> dynamicPenalty = null, CancellationToken cancellation = default,
            string[] passengerStops = null)
        {
            // This shortest movement search can stage conflicting switch
            // settings after arrival. It does not enumerate every historical
            // switch combination in the simultaneous-reservation solver.
            return FindStagedRouteVia(from, to, via, direction, startSpan, dynamicPenalty, cancellation,
                passengerStops ?? Array.Empty<string>(), true);
        }

        /// <summary>
        /// Finds a bounded movement walk when one continuous reservation is
        /// impossible because the walk revisits a physical rail or changes a
        /// turnout/turntable phase.  The returned plan keeps one logical route
        /// identity and contains independently reservable stages.  Traversal
        /// identity is oriented (rail + exit), so an opposite-direction pass
        /// is a legitimate movement while an identical oriented pass is
        /// rejected; this is what prevents useless infinite loops.
        /// </summary>
        public RoutePlan FindStagedRouteVia(string from, string to, string[] via, int direction = 0,
            double? startSpan = null, Func<string,double> dynamicPenalty = null, CancellationToken cancellation = default,
            string[] passengerStops = null, bool allowSingleStage = false)
        {
            via ??= Array.Empty<string>();
            if (via.Length > 128 || from == null || to == null || !Tracks.ContainsKey(from) || direction < -1 || direction > 1 ||
                startSpan.HasValue && !Finite(startSpan.Value)) return null;
            var required = via.Concat(new[] { to }).ToArray();
            if (required.Any(id => string.IsNullOrEmpty(id) || !Tracks.ContainsKey(id))) return null;

            var turnbacks = new HashSet<string>(passengerStops ?? Array.Empty<string>(), StringComparer.Ordinal);
            var labels = new List<TraversalLabel>();
            var heap = new MinHeap();
            var expanded = new Dictionary<string, double>(StringComparer.Ordinal);
            void Add(TraversalLabel label)
            {
                if (label.depth > 4096) return;
                // Once native settings can change between stages, historical
                // switch choices cannot constrain the future walk. Positive
                // edge costs and one best label per oriented track/task phase
                // prevent unproductive cycles without exponential path sets.
                var signature = label.stage + "|" + label.rail + ":" + label.exit;
                if (expanded.TryGetValue(signature, out var previousCost) && previousCost <= label.cost) return;
                expanded[signature] = label.cost;
                label.signature = signature;
                label.index = labels.Count;
                labels.Add(label);
                heap.Push(label.index.ToString(), label.cost);
            }
            for (var end = 0; end < 2; end++)
            {
                if (direction != 0 && (end == 1 ? 1 : -1) != direction) continue;
                var length = startSpan.HasValue
                    ? (end == 0 ? startSpan.Value : Tracks[from].length - startSpan.Value)
                    : Tracks[from].length;
                Add(new TraversalLabel
                {
                    rail = from, exit = end, stage = from == to && via.Length == 0 ? 1 : 0,
                    cost = Math.Max(0, Math.Min(Tracks[from].length, length)) + Tracks[from].routePenalty,
                    depth = 1,
                });
            }
            TraversalLabel goal = null;
            while (heap.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var current = labels[int.Parse(heap.Pop().Key)];
                if (expanded.TryGetValue(current.signature, out var bestCost) && current.cost > bestCost + 1e-9)
                    continue;
                if (current.stage == required.Length) { goal = current; break; }
                // Reversal is not an invented track edge. It is an explicit
                // stop boundary: arrival and departure reserve separate native
                // paths, and no connection is created between their directions.
                if (!current.reversal && current.stage > 0 && required[current.stage - 1] == current.rail &&
                    turnbacks.Contains(current.rail))
                    Add(new TraversalLabel { rail = current.rail, exit = 1 - current.exit, stage = current.stage,
                        previous = current, reversal = true, cost = current.cost + Tracks[current.rail].length,
                        depth = current.depth + 1 });
                foreach (var link in RouteLinks(current.rail, current.exit))
                {
                    // Only the next mandatory stop advances the task phase.
                    // Array.IndexOf is incorrect when a passenger order
                    // legitimately visits the same rail twice; it always
                    // returns the first occurrence and made the second stop
                    // unreachable.
                    var at = current.stage < required.Length && required[current.stage] == link.track
                        ? current.stage : -1;
                    if (at < 0 && required.Contains(link.track)) continue;
                    var nextExit = 1 - link.end;
                    var extra = dynamicPenalty?.Invoke(link.track) ?? 0;
                    if (double.IsPositiveInfinity(extra)) continue;
                    if (!Finite(extra) || extra < 0) extra = 0;
                    Add(new TraversalLabel
                    {
                        rail = link.track, exit = nextExit,
                        stage = current.stage + (at == current.stage ? 1 : 0),
                        cost = current.cost + Tracks[link.track].length + Tracks[link.track].routePenalty + extra + link.crossingLength,
                        previous = current, link = link, depth = current.depth + 1,
                    });
                }
            }
            return goal == null ? null : LabelStagedPlan(goal, from, to, via, startSpan, allowSingleStage);
        }

        private sealed class TraversalLabel
        {
            internal string rail;
            internal int exit, stage, index;
            internal double cost;
            internal TraversalLabel previous;
            internal Link link;
            internal bool reversal;
            internal int depth;
            internal string signature;
        }

        private RoutePlan LabelStagedPlan(TraversalLabel goal, string from, string to, string[] via, double? startSpan, bool allowSingleStage)
        {
            var nodes = new List<TraversalLabel>();
            for (var n = goal; n != null; n = n.previous) nodes.Add(n);
            nodes.Reverse();
            var ids = nodes.Select(n => n.rail).ToArray();
            var directions = nodes.Select(n => n.exit == 1 ? 1 : -1).ToArray();
            if (ids.Length == 0) return null;

            var links = nodes.Skip(1).Select(n => n.link).ToArray();
            var ranges = new List<Tuple<int,int>>();
            var segmentStart = 0;
            var usedTracks = new HashSet<string>(StringComparer.Ordinal) { ids[0] };
            var usedSwitches = new Dictionary<string, int>(StringComparer.Ordinal);
            var usedTables = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 1; i < ids.Length; i++)
            {
                var link = links[i - 1];
                if (nodes[i].reversal)
                {
                    ranges.Add(Tuple.Create(segmentStart, i - 1));
                    segmentStart = i;
                    usedTracks.Clear(); usedSwitches.Clear(); usedTables.Clear();
                    usedTracks.Add(ids[i]);
                    continue;
                }
                var repeatedTrack = usedTracks.Contains(ids[i]);
                var phaseChange = repeatedTrack ||
                    link.junction != null && usedSwitches.TryGetValue(link.junction, out var branch) && branch != link.branch ||
                    link.turntable != null && usedTables.Contains(link.turntable);
                if (phaseChange)
                {
                    // End before the repeated traversal and carry the last
                    // rail into the next leg.  This preserves the physical
                    // connection while ensuring each native reservation leg
                    // contains every rail at most once.
                    var end = i - 1;
                    if (end >= segmentStart) ranges.Add(Tuple.Create(segmentStart, end));
                    segmentStart = i - 1;
                    usedTracks.Clear(); usedSwitches.Clear(); usedTables.Clear();
                    usedTracks.Add(ids[segmentStart]);
                }
                usedTracks.Add(ids[i]);
                if (link.junction != null) usedSwitches[link.junction] = link.branch;
                if (link.turntable != null) usedTables.Add(link.turntable);
            }
            if (segmentStart <= ids.Length - 1) ranges.Add(Tuple.Create(segmentStart, ids.Length - 1));
            var stages = new List<RouteStage>();
            for (var s = 0; s < ranges.Count; s++)
            {
                var first = ranges[s].Item1;
                var last = ranges[s].Item2;
                if (last < first) continue;
                var stageIds = ids.Skip(first).Take(last - first + 1).ToArray();
                var stageDirs = directions.Skip(first).Take(last - first + 1).ToArray();
                var stageLinks = links.Skip(first).Take(last - first).ToArray();
                var steps = new List<RouteStep>();
                var tables = new List<TurntableStep>();
                for (var i = 0; i < stageLinks.Length; i++)
                {
                    var link = stageLinks[i];
                    if (link.junction != null && steps.All(x => x.id != link.junction))
                        steps.Add(new RouteStep { id = link.junction, branch = link.branch });
                    if (link.turntable != null && tables.All(x => x.id != link.turntable))
                        tables.Add(new TurntableStep { id = link.turntable, position = link.position,
                            from = stageIds[i], fromEnd = stageDirs[i] > 0 ? 1 : 0,
                            to = stageIds[i + 1], toEnd = link.end });
                }
                var origin = s == 0 ? startSpan ?? (stageDirs[0] > 0 ? 0 : Tracks[stageIds[0]].length) : stageDirs[0] > 0 ? 0 : Tracks[stageIds[0]].length;
                var length = Math.Max(0, stageDirs[0] > 0 ? Tracks[stageIds[0]].length - origin : origin);
                for (var i = 0; i < stageLinks.Length; i++) length += Tracks[stageIds[i + 1]].length + stageLinks[i].crossingLength;
                stages.Add(new RouteStage { id = "stage-" + s, from = stageIds[0], to = stageIds[stageIds.Length - 1],
                    status = s == 0 ? "active" : "pending", tracks = stageIds, directions = stageDirs,
                    switches = steps.ToArray(), turntables = tables.ToArray(), length = length, remaining = length, startSpan = origin });
            }
            if (stages.Count == 0 || stages.Count == 1 && !allowSingleStage || stages.Count > 128) return null;
            var firstStage = stages[0];
            return new RoutePlan { from = from, to = to, activeFrom = firstStage.from, activeTo = firstStage.to,
                via = via.ToArray(), tracks = firstStage.tracks.ToArray(), directions = firstStage.directions.ToArray(),
                switches = firstStage.switches, turntables = firstStage.turntables, length = firstStage.length,
                remaining = firstStage.remaining, startSpan = firstStage.startSpan, warnings = Array.Empty<string>(),
                staged = stages.Count > 1, stageStatus = stages.Count > 1 ? "active" : "single",
                stagedReason = stages.Count > 1 ? "ROUTE_MANEUVER_REQUIRED" : null, stageIndex = 0,
                stages = stages.Count > 1 ? stages.ToArray() : Array.Empty<RouteStage>() };
        }

        // Diagnostic reachability follows only native oriented connections.
        // Reusing a rail or changing a turnout later requires separate movement
        // phases, which cannot be reserved as one simultaneous switch plan.
        public bool HasOrderedWalk(string from,string to,string[] via,int direction=0,CancellationToken cancellation=default)
        {
            if(from==null||to==null||!Tracks.ContainsKey(from)||!Tracks.ContainsKey(to))return false;
            var required=(via??Array.Empty<string>()).Concat(new[]{to}).ToArray();
            if(required.Any(id=>id==null||!Tracks.ContainsKey(id)))return false;
            var queue=new Queue<Tuple<string,int,int>>();var seen=new HashSet<string>();
            if(direction<=0)queue.Enqueue(Tuple.Create(from,0,0));
            if(direction>=0)queue.Enqueue(Tuple.Create(from,1,0));
            while(queue.Count>0) {
                cancellation.ThrowIfCancellationRequested();
                var n=queue.Dequeue();string rail=n.Item1;int exit=n.Item2,stage=n.Item3;
                if(!seen.Add(stage+"|"+rail+":"+exit))continue;
                if(stage==required.Length)return true;
                foreach(var link in RouteLinks(rail,exit)) {
                    int at=Array.IndexOf(required,link.track);if(at>=0&&at!=stage)continue;
                    queue.Enqueue(Tuple.Create(link.track,1-link.end,stage+(at==stage?1:0)));
                }
            }
            return false;
        }

        private sealed class PathLabel
        {
            internal string rail;
            internal int exit, stage, index;
            internal double cost;
            internal PathLabel previous;
            internal Link link;
            internal HashSet<string> tracks, tables;
            internal Dictionary<string,int> switches;
            internal bool superseded;
        }
        private static bool Dominates(PathLabel a,PathLabel b) => a.cost<=b.cost &&
            a.tracks.IsSubsetOf(b.tracks) && a.tables.IsSubsetOf(b.tables) &&
            a.switches.All(s=>b.switches.TryGetValue(s.Key,out var branch)&&branch==s.Value);

        private RoutePlan FindConstrained(string from,string to,string[] via,int direction,double? startSpan,
            Func<string,double> penalty,CancellationToken cancellation)
        {
            var required=via.Concat(new[]{to}).ToArray();
            // Exact optimistic distance to the next required track, computed
            // backwards on oriented links. A* expands useful prefixes first.
            // Ignoring visited rails/switch constraints makes this a lower bound.
            var reverse=new Dictionary<string,List<Tuple<string,double>>>();
            foreach(var rail in Tracks.Keys)for(int exit=0;exit<2;exit++)foreach(var link in RouteLinks(rail,exit)) {
                double extra=penalty?.Invoke(link.track)??0;if(double.IsPositiveInfinity(extra))continue;
                if(!Finite(extra)||extra<0)extra=0;
                string next=link.track+":"+(1-link.end);
                if(!reverse.TryGetValue(next,out var incoming))reverse[next]=incoming=new List<Tuple<string,double>>();
                incoming.Add(Tuple.Create(rail+":"+exit,Tracks[link.track].length+Tracks[link.track].routePenalty+extra+link.crossingLength));
            }
            var estimates=new Dictionary<string,double>[required.Length];
            for(int stage=required.Length-1;stage>=0;stage--) {
                cancellation.ThrowIfCancellationRequested();
                var distances=estimates[stage]=new Dictionary<string,double>();var work=new MinHeap();
                for(int exit=0;exit<2;exit++) {
                    string key=required[stage]+":"+exit;
                    double tail=stage+1==required.Length?0:estimates[stage+1].TryGetValue(key,out var d)?d:double.PositiveInfinity;
                    if(double.IsPositiveInfinity(tail))continue;distances[key]=tail;work.Push(key,tail);
                }
                while(work.Count>0) {
                    cancellation.ThrowIfCancellationRequested();
                    var n=work.Pop();if(distances[n.Key]!=n.Value || !reverse.TryGetValue(n.Key,out var incoming))continue;
                    foreach(var edge in incoming) {
                        double d=n.Value+edge.Item2;
                        if(distances.TryGetValue(edge.Item1,out var old)&&old<=d)continue;
                        distances[edge.Item1]=d;work.Push(edge.Item1,d);
                    }
                }
            }
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var labels=new List<PathLabel>();var frontiers=new Dictionary<string,List<PathLabel>>();var heap=new MinHeap();
            void Add(PathLabel label) {
                double estimate=0;
                if(label.stage<required.Length&&!estimates[label.stage].TryGetValue(label.rail+":"+label.exit,out estimate))return;
                string key=label.stage+"|"+label.rail+":"+label.exit;
                if(!frontiers.TryGetValue(key,out var prior))frontiers[key]=prior=new List<PathLabel>();
                if(prior.Any(p=>Dominates(p,label)))return;
                foreach(var p in prior.Where(p=>Dominates(label,p)).ToArray()){p.superseded=true;prior.Remove(p);}
                if(labels.Count>=8192 || timer.ElapsedMilliseconds>1000)throw new RouteSearchLimitException();
                label.index=labels.Count;labels.Add(label);prior.Add(label);heap.Push(label.index.ToString(),label.cost+estimate);
            }
            for(int end=0;end<2;end++)if(direction==0||(end==1?1:-1)==direction) {
                double length=startSpan.HasValue?(end==0?startSpan.Value:Tracks[from].length-startSpan.Value):Tracks[from].length;
                Add(new PathLabel{rail=from,exit=end,stage=from==to&&via.Length==0?1:0,
                    cost=Math.Max(0,Math.Min(Tracks[from].length,length))+Tracks[from].routePenalty,
                    tracks=new HashSet<string>{from},tables=new HashSet<string>(),switches=new Dictionary<string,int>()});
            }
            while(heap.Count>0) {
                cancellation.ThrowIfCancellationRequested();
                var current=labels[int.Parse(heap.Pop().Key)];if(current.superseded)continue;
                if(current.stage==required.Length)return LabelPlan(current,from,to,via,startSpan);
                foreach(var link in RouteLinks(current.rail,current.exit)) {
                    if(current.tracks.Contains(link.track))continue;
                    if(link.junction!=null && current.switches.TryGetValue(link.junction,out var branch)&&branch!=link.branch)continue;
                    if(link.turntable!=null && current.tables.Contains(link.turntable))continue;
                    int at=Array.IndexOf(required,link.track);if(at>=0 && at!=current.stage)continue;
                    double extra=penalty?.Invoke(link.track)??0;if(double.IsPositiveInfinity(extra))continue;
                    if(!Finite(extra)||extra<0)extra=0;
                    var next=new PathLabel{rail=link.track,exit=1-link.end,stage=current.stage+(at==current.stage?1:0),
                        cost=current.cost+Tracks[link.track].length+Tracks[link.track].routePenalty+extra+link.crossingLength,
                        previous=current,link=link,tracks=new HashSet<string>(current.tracks),tables=new HashSet<string>(current.tables),
                        switches=new Dictionary<string,int>(current.switches)};
                    next.tracks.Add(link.track);
                    if(link.junction!=null)next.switches[link.junction]=link.branch;
                    if(link.turntable!=null)next.tables.Add(link.turntable);
                    Add(next);
                }
            }
            return null;
        }
        private RoutePlan LabelPlan(PathLabel goal,string from,string to,string[] via,double? startSpan)
        {
            var nodes=new List<PathLabel>();for(var n=goal;n!=null;n=n.previous)nodes.Add(n);nodes.Reverse();
            var ids=nodes.Select(n=>n.rail).ToArray();var directions=nodes.Select(n=>n.exit==1?1:-1).ToArray();
            double origin=startSpan??(directions[0]>0?0:Tracks[from].length);
            double length=Math.Max(0,directions[0]>0?Tracks[from].length-origin:origin);
            var switches=new List<RouteStep>();var tables=new List<TurntableStep>();
            foreach(var node in nodes.Skip(1)) {
                var link=node.link;length+=Tracks[node.rail].length+link.crossingLength;
                if(link.junction!=null && switches.All(s=>s.id!=link.junction))switches.Add(new RouteStep{id=link.junction,branch=link.branch});
                if(link.turntable!=null)tables.Add(new TurntableStep{id=link.turntable,position=link.position,from=node.previous.rail,fromEnd=node.previous.exit,to=node.rail,toEnd=link.end});
            }
            return new RoutePlan{from=from,to=to,via=via.ToArray(),tracks=ids,directions=directions,switches=switches.ToArray(),turntables=tables.ToArray(),
                length=length,remaining=length,startSpan=origin,warnings=Array.Empty<string>()};
        }
    }
}
