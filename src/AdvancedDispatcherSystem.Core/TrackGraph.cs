using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AdvancedDispatcherSystem.Core
{
    public sealed partial class TrackGraph
    {
        public readonly Dictionary<string, TrackDef> Tracks = new Dictionary<string, TrackDef>(StringComparer.Ordinal);
        public readonly Dictionary<string, JunctionDef> Junctions = new Dictionary<string, JunctionDef>(StringComparer.Ordinal);
        public readonly Dictionary<string, TurntableDef> Turntables = new Dictionary<string, TurntableDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Link>> tableRoutes = new Dictionary<string, List<Link>>();
        private readonly HashSet<string> bridges = new HashSet<string>();
        public TrackGraph(Topology topology)
        {
            if (topology == null || topology.tracks == null || topology.junctions == null) throw new ArgumentException("INCOMPLETE_TOPOLOGY");
            foreach (var t in topology.tracks)
            {
                if (t == null || string.IsNullOrEmpty(t.id) || !Finite(t.length) || t.length < 0 || !Finite(t.routePenalty) || t.routePenalty < 0 || t.points == null || t.points.Length < 4 || t.points.Length % 2 != 0) throw new ArgumentException("INVALID_TRACK");
                foreach (var p in t.points) if (!Finite(p)) throw new ArgumentException("INVALID_GEOMETRY");
                if (Tracks.ContainsKey(t.id) || t.a == null || t.b == null) throw new ArgumentException("INVALID_TRACK");
                if (t.spans != null && t.spans.Length > 0) {
                    if (t.spans.Length != t.points.Length / 2) throw new ArgumentException("INVALID_SPANS");
                    double previous = -1;
                    foreach (var span in t.spans) { if (!Finite(span) || span < previous || span < 0 || span > t.length + 1) throw new ArgumentException("INVALID_SPANS"); previous = span; }
                }
                Tracks.Add(t.id, t);
            }
            foreach (var j in topology.junctions) {
                if (j == null || string.IsNullOrEmpty(j.id) || !Finite(j.x) || !Finite(j.z) || j.branches == null || Junctions.ContainsKey(j.id)) throw new ArgumentException("INVALID_JUNCTION");
                Junctions.Add(j.id, j);
            }
            foreach (var t in Tracks.Values)
                foreach (var links in new[] { t.a, t.b })
                    foreach (var link in links)
                        if (link == null || string.IsNullOrEmpty(link.track) || !Tracks.ContainsKey(link.track) || link.end < 0 || link.end > 1 || (!string.IsNullOrEmpty(link.junction) && (!Junctions.ContainsKey(link.junction) || link.branch < 0 || link.branch >= Junctions[link.junction].branches.Length)))
                            throw new ArgumentException("INVALID_CONNECTION");
            foreach (var station in topology.stations ?? new StationDef[0])
            {
                if (station == null || string.IsNullOrEmpty(station.id) || station.tracks == null || !Finite(station.x) || !Finite(station.z)) throw new ArgumentException("INVALID_STATION");
                foreach (var id in station.tracks) if (string.IsNullOrEmpty(id) || !Tracks.ContainsKey(id)) throw new ArgumentException("INVALID_STATION_TRACK");
                foreach (var id in station.spawnTracks ?? new string[0]) if (string.IsNullOrEmpty(id) || !Tracks.ContainsKey(id)) throw new ArgumentException("INVALID_STATION_SPAWN_TRACK");
            }
            foreach (var table in topology.turntables ?? new TurntableDef[0])
            {
                if (table == null || string.IsNullOrEmpty(table.id) || string.IsNullOrEmpty(table.track) || !Tracks.ContainsKey(table.track) || table.ends == null || !Finite(table.x) || !Finite(table.z) || !Finite(table.radius) || table.radius <= 0) throw new ArgumentException("INVALID_TURNTABLE");
                Turntables.Add(table.id, table); bridges.Add(table.track);
                foreach (var end in table.ends)
                    if (end == null || string.IsNullOrEmpty(end.track) || !Tracks.ContainsKey(end.track) || end.end < 0 || end.end > 1 || !Finite(end.angle)) throw new ArgumentException("INVALID_TURNTABLE_END");
                for (int i = 0; i < table.ends.Length; i++)
                {
                    var from = table.ends[i];
                    string key = from.track + ":" + from.end;
                    if (!tableRoutes.TryGetValue(key, out var links)) tableRoutes[key] = links = new List<Link>();
                    for (int j = 0; j < table.ends.Length; j++)
                    {
                        var to = table.ends[j];
                        if (to != null && from.track != to.track && TurntableRules.TryAlignment(table,i,j,0,out _))
                            links.Add(new Link { track = to.track, end = to.end, turntable = table.id, position = i, crossingLength = Tracks[table.track].length });
                    }
                }
            }
        }
        public IEnumerable<Link> RouteLinks(string id, int end)
        {
            // Pass-through planning considers only physically opposite mouths.
            // Actual live bridge links remain separate for occupancy/footprints.
            if (bridges.Contains(id)) yield break;
            foreach (var link in end == 0 ? Tracks[id].a : Tracks[id].b)
                if (!bridges.Contains(link.track)) yield return link;
            if (tableRoutes.TryGetValue(id + ":" + end, out var candidates)) foreach (var link in candidates) yield return link;
        }
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public string RouteFailure(string from, string to, int direction = 0) {
            if(from==null || to==null || !Tracks.ContainsKey(from) || !Tracks.ContainsKey(to)) return "NO_ROUTE";
            // Diagnostic only: allow a stopped train to turn on a bridge. This
            // relaxed search never creates an executable route or rotates it.
            var queue=new Queue<string>();var visited=new HashSet<string>();
            if(direction<=0)queue.Enqueue(from+":0");if(direction>=0)queue.Enqueue(from+":1");
            while(queue.Count>0) {
                var node=queue.Dequeue();if(!visited.Add(node))continue;
                string id=node.Substring(0,node.Length-2);int end=node[node.Length-1]-'0';
                if(id==to) return "TURNTABLE_MANEUVER_REQUIRED";
                foreach(var link in RouteLinks(id,end)) queue.Enqueue(link.track+":"+(1-link.end));
                foreach(var table in Turntables.Values) {
                    bool onBridge=table.track==id;
                    bool mouth=Array.Exists(table.ends,e=>e.track==id&&e.end==end);
                    if(!onBridge&&!mouth)continue;
                    foreach(var target in table.ends) if(target.track!=id)queue.Enqueue(target.track+":"+(1-target.end));
                    if(to==table.track)return "TURNTABLE_MANEUVER_REQUIRED";
                }
            }
            return "NO_ROUTE";
        }

        // Include short rails between bogies: testing only the bogie tracks misses a car spanning a turnout.
        // Returns null when the two samples cannot be joined within the physical car length.
        public string[] Footprint(CarState car)
        {
            if (!Tracks.TryGetValue(car.track1 ?? "", out var start) || !Tracks.ContainsKey(car.track2 ?? "")) return null;
            if (car.track1 == car.track2) return new[] { car.track1 };
            var queue = new Queue<Tuple<string, int, double, List<string>>>();
            queue.Enqueue(Tuple.Create(car.track1, 0, Math.Max(0, car.span1), new List<string> { car.track1 }));
            queue.Enqueue(Tuple.Create(car.track1, 1, Math.Max(0, start.length - car.span1), new List<string> { car.track1 }));
            var visited = new HashSet<string>(); int budget = 256;
            while (queue.Count > 0 && budget-- > 0)
            {
                var node = queue.Dequeue();
                if (node.Item3 > car.length + 3 || !visited.Add(node.Item1 + ":" + node.Item2)) continue;
                foreach (var link in node.Item2 == 0 ? Tracks[node.Item1].a : Tracks[node.Item1].b)
                {
                    var path = new List<string>(node.Item4) { link.track };
                    if (link.track == car.track2)
                    {
                        double distance = link.end == 0 ? car.span2 : Tracks[link.track].length - car.span2;
                        if (node.Item3 + Math.Max(0, distance) <= car.length + 3) return path.ToArray();
                    }
                    else queue.Enqueue(Tuple.Create(link.track, 1 - link.end, node.Item3 + Tracks[link.track].length, path));
                }
            }
            return null;
        }

        // Include the measured body/coupler overhang beyond each bogie. This
        // overload is used for tail clearance; unknown geometry returns null
        // and cannot authorize release. At junctions all reachable overhang
        // rails are retained until the body is clear of the fouling point.
        public string[] Footprint(CarState car, double frontOverhang, double rearOverhang)
        {
            var middle=Footprint(car);
            if(middle==null || !Finite(frontOverhang) || !Finite(rearOverhang) || frontOverhang<0 || rearOverhang<0)return null;
            var result=new HashSet<string>(middle,StringComparer.Ordinal);
            bool Extend(string track,double span,double margin,bool front)
            {
                if(margin<=0)return true;
                int outward;
                if(car.track1==car.track2) {
                    if(Math.Abs(car.span1-car.span2)<.001)return false;
                    outward=(car.span1>car.span2)==front?1:0;
                } else {
                    var adjacent=front?middle[1]:middle[middle.Length-2];
                    var end0=Tracks[track].a.Any(l=>l.track==adjacent);
                    var end1=Tracks[track].b.Any(l=>l.track==adjacent);
                    if(end0==end1)return false;
                    outward=end0?1:0;
                }
                var queue=new Queue<Tuple<string,int,double>>();
                queue.Enqueue(Tuple.Create(track,outward,margin-(outward==0?span:Tracks[track].length-span)));
                var visited=new Dictionary<string,double>();
                while(queue.Count>0) {
                    var node=queue.Dequeue();if(node.Item3<0)continue;
                    var key=node.Item1+":"+node.Item2;
                    if(visited.TryGetValue(key,out var prior)&&prior>=node.Item3)continue;
                    visited[key]=node.Item3;
                    foreach(var link in node.Item2==0?Tracks[node.Item1].a:Tracks[node.Item1].b) {
                        if(!Tracks.TryGetValue(link.track,out var next))return false;
                        result.Add(link.track);
                        queue.Enqueue(Tuple.Create(link.track,1-link.end,node.Item3-next.length));
                    }
                }
                return true;
            }
            return Extend(car.track1,car.span1,frontOverhang,true)&&Extend(car.track2,car.span2,rearOverhang,false)?result.ToArray():null;
        }

        // Dijkstra on oriented track traversals. A crossing without an endpoint link is never a connection.
        private RoutePlan FindRouteUnconstrained(string from, string to, int direction = 0, double? startSpan = null, Func<string, double> dynamicPenalty = null, CancellationToken cancellation = default)
        {
            if (direction < -1 || direction > 1 || startSpan.HasValue && !Finite(startSpan.Value)) return null;
            if (from == null || to == null || !Tracks.ContainsKey(from) || !Tracks.ContainsKey(to)) return null;
            var distance = new Dictionary<string, double>();
            var previous = new Dictionary<string, Tuple<string, Link>>();
            var heap = new MinHeap();
            for (int end = 0; end < 2; end++) { if (direction != 0 && (end == 1 ? 1 : -1) != direction) continue; string key = from + ":" + end; double cost = startSpan.HasValue ? (end == 0 ? startSpan.Value : Tracks[from].length - startSpan.Value) : Tracks[from].length; cost = Math.Max(0, Math.Min(Tracks[from].length, cost)) + Tracks[from].routePenalty; distance[key] = cost; heap.Push(key, cost); }
            string goal = null;
            while (heap.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var item = heap.Pop();
                if (distance[item.Key] != item.Value) continue;
                string id = item.Key.Substring(0, item.Key.Length - 2);
                int exit = item.Key[item.Key.Length - 1] - '0';
                if (id == to) { goal = item.Key; break; }
                foreach (var link in RouteLinks(id, exit))
                {
                    string next = link.track + ":" + (1 - link.end);
                    double extra = dynamicPenalty == null ? 0 : dynamicPenalty(link.track);
                    if (double.IsPositiveInfinity(extra)) continue; // Hard obstruction during route editing.
                    if (!Finite(extra) || extra < 0) extra = 0;
                    double cost = item.Value + Tracks[link.track].length + Tracks[link.track].routePenalty + extra + link.crossingLength;
                    if (distance.TryGetValue(next, out var old) && old <= cost) continue;
                    distance[next] = cost; previous[next] = Tuple.Create(item.Key, link); heap.Push(next, cost);
                }
            }
            if (goal == null) return null;
            double length = 0;
            var tracks = new List<string>(); var directions = new List<int>(); var steps = new List<RouteStep>(); var settings = new Dictionary<string, int>();
            var tableSteps = new List<TurntableStep>();
            string walk = goal;
            while (true)
            {
                tracks.Add(walk.Substring(0, walk.Length - 2));
                directions.Add(walk[walk.Length - 1] == '1' ? 1 : -1);
                if (!previous.TryGetValue(walk, out var p)) break;
                if (p.Item2.turntable != null)
                {
                    if (tableSteps.Exists(x => x.id == p.Item2.turntable)) return null;
                    tableSteps.Add(new TurntableStep { id = p.Item2.turntable, position = p.Item2.position, from = p.Item1.Substring(0, p.Item1.Length - 2), fromEnd = p.Item1[p.Item1.Length-1]-'0', to = p.Item2.track, toEnd = p.Item2.end });
                }
                if (!string.IsNullOrEmpty(p.Item2.junction))
                {
                    if (settings.TryGetValue(p.Item2.junction, out int branch) && branch != p.Item2.branch) return null;
                    if (!settings.ContainsKey(p.Item2.junction)) steps.Add(new RouteStep { id = p.Item2.junction, branch = p.Item2.branch });
                    settings[p.Item2.junction] = p.Item2.branch;
                }
                walk = p.Item1;
            }
            tracks.Reverse(); directions.Reverse(); steps.Reverse();
            tableSteps.Reverse();
            for (int i = 0; i < tracks.Count; i++)
            {
                var current = Tracks[tracks[i]];
                double origin = i == 0 ? (startSpan ?? (directions[i] > 0 ? 0 : current.length)) : directions[i] > 0 ? 0 : current.length;
                length += Math.Max(0, directions[i] > 0 ? current.length - origin : origin);
                if (i + 1 < tracks.Count)
                {
                    var link = RouteLinks(tracks[i], directions[i] > 0 ? 1 : 0).FirstOrDefault(l => l.track == tracks[i + 1] && (l.end == 0 ? 1 : -1) == directions[i + 1]);
                    if (link != null) length += link.crossingLength;
                }
            }
            return new RoutePlan { from = from, to = to, tracks = tracks.ToArray(), directions = directions.ToArray(), switches = steps.ToArray(), turntables = tableSteps.ToArray(), length = length, remaining = length, startSpan = startSpan ?? (directions[0] > 0 ? 0 : Tracks[from].length), warnings = new string[0] };
        }

        /// <summary>
        /// Resolve one continuous route through an ordered list of required
        /// tracks. Repeated tracks, conflicting turnout branches and a
        /// turntable used twice are rejected instead of producing a cyclic or
        /// partially executable plan.
        /// </summary>
        private RoutePlan FindViaUnconstrained(string from, string to, string[] via, int direction = 0, double? startSpan = null, Func<string, double> dynamicPenalty = null, CancellationToken cancellation = default)
        {
            if (via != null && via.Length > 128) return null;
            var points = new List<string> { from };
            if (via != null) points.AddRange(via);
            points.Add(to);
            if (points.Any(string.IsNullOrEmpty) || points.Any(id => !Tracks.ContainsKey(id))) return null;
            if (points.Count == 2) return FindRouteUnconstrained(from, to, direction, startSpan, dynamicPenalty, cancellation);
            if (points.Count != points.Distinct(StringComparer.Ordinal).Count()) return null;

            if (direction < -1 || direction > 1 || startSpan.HasValue && !Finite(startSpan.Value)) return null;
            // Search the whole ordered request. Greedily choosing START -> VIA
            // first can arrive at VIA in a direction from which DESTINATION is
            // unreachable, even though another approach gives a valid route.
            var required = points.Skip(1).ToArray();
            string Key(string id, int exit, int stage) => stage + "|" + id + ":" + exit;
            string Rail(string key) => key.Substring(key.IndexOf('|') + 1, key.Length - key.IndexOf('|') - 3);
            int Exit(string key) => key[key.Length-1]-'0';
            int Stage(string key) => int.Parse(key.Substring(0,key.IndexOf('|')));
            var distance = new Dictionary<string,double>();
            var previous = new Dictionary<string,Tuple<string,Link>>();
            var heap = new MinHeap();
            for(int end=0;end<2;end++) {
                if(direction!=0 && (end==1?1:-1)!=direction)continue;
                string key=Key(from,end,0);
                double cost=startSpan.HasValue?(end==0?startSpan.Value:Tracks[from].length-startSpan.Value):Tracks[from].length;
                distance[key]=Math.Max(0,Math.Min(Tracks[from].length,cost))+Tracks[from].routePenalty;
                heap.Push(key,distance[key]);
            }
            string goal=null;
            while(heap.Count>0) {
                cancellation.ThrowIfCancellationRequested();
                var item=heap.Pop();if(distance[item.Key]!=item.Value)continue;
                string rail=Rail(item.Key);int stage=Stage(item.Key),exit=Exit(item.Key);
                if(stage==required.Length) {goal=item.Key;break;}
                foreach(var link in RouteLinks(rail,exit)) {
                    int requiredAt=Array.IndexOf(required,link.track);
                    if(requiredAt>=0 && requiredAt!=stage)continue; // Cannot skip or revisit mandatory points.
                    int nextStage=stage+(requiredAt==stage?1:0);
                    double extra=dynamicPenalty?.Invoke(link.track)??0;
                    if(double.IsPositiveInfinity(extra))continue;
                    if(!Finite(extra)||extra<0)extra=0;
                    string next=Key(link.track,1-link.end,nextStage);
                    double cost=item.Value+Tracks[link.track].length+Tracks[link.track].routePenalty+extra+link.crossingLength;
                    if(distance.TryGetValue(next,out var old)&&old<=cost)continue;
                    distance[next]=cost;previous[next]=Tuple.Create(item.Key,link);heap.Push(next,cost);
                }
            }
            if(goal==null)return null;
            var nodes=new List<string>();var links=new List<Link>();string walk=goal;
            while(true) {nodes.Add(walk);if(!previous.TryGetValue(walk,out var prev))break;links.Add(prev.Item2);walk=prev.Item1;}
            nodes.Reverse();links.Reverse();
            var ids=nodes.Select(Rail).ToArray();var dirs=nodes.Select(k=>Exit(k)==1?1:-1).ToArray();
            // A single reservation is a continuous movement with one setting
            // per switch. Shunting reversals remain separate task movements.
            if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length)return null;
            var steps=new List<RouteStep>();var tables=new List<TurntableStep>();
            double origin=startSpan??(dirs[0]>0?0:Tracks[from].length);
            double length=Math.Max(0,dirs[0]>0?Tracks[from].length-origin:origin);
            for(int i=0;i<links.Count;i++) {
                var link=links[i];length+=Tracks[ids[i+1]].length+link.crossingLength;
                if(link.junction!=null) {
                    var prior=steps.FirstOrDefault(x=>x.id==link.junction);
                    if(prior!=null && prior.branch!=link.branch)return null;
                    if(prior==null)steps.Add(new RouteStep{id=link.junction,branch=link.branch});
                }
                if(link.turntable!=null) {
                    if(tables.Any(x=>x.id==link.turntable))return null;
                    tables.Add(new TurntableStep{id=link.turntable,position=link.position,from=ids[i],to=ids[i+1],fromEnd=Exit(nodes[i]),toEnd=link.end});
                }
            }
            return new RoutePlan {from=from,to=to,via=via.ToArray(),tracks=ids,directions=dirs,switches=steps.ToArray(),turntables=tables.ToArray(),
                length=length,remaining=length,startSpan=origin,warnings=new string[0]};
        }

        private sealed class MinHeap
        {
            private readonly List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            public int Count => list.Count;
            public void Push(string id, double distance)
            {
                var item = new KeyValuePair<string, double>(id, distance); int i = list.Count; list.Add(item);
                while (i > 0) { int parent = (i - 1) / 2; if (list[parent].Value <= distance) break; list[i] = list[parent]; i = parent; }
                list[i] = item;
            }
            public KeyValuePair<string, double> Pop()
            {
                var result = list[0]; var item = list[list.Count - 1]; list.RemoveAt(list.Count - 1); if (list.Count == 0) return result;
                int i = 0;
                while (i * 2 + 1 < list.Count)
                {
                    int child = i * 2 + 1; if (child + 1 < list.Count && list[child + 1].Value < list[child].Value) child++;
                    if (list[child].Value >= item.Value) break; list[i] = list[child]; i = child;
                }
                list[i] = item; return result;
            }
        }
    }
}
