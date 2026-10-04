using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private RailTrack[] tracks = new RailTrack[0];
        private RailTrackBogiesOnTrack[] trackOccupancy = new RailTrackBogiesOnTrack[0];
        private Junction[] worldJunctions = new Junction[0];
        private int[] linkSignatures = new int[0];
        private readonly Dictionary<RailTrack, string> trackIds = new Dictionary<RailTrack, string>();
        private readonly Dictionary<Junction, string> junctionIds = new Dictionary<Junction, string>();
        private readonly Dictionary<string, Junction> junctionById = new Dictionary<string, Junction>();
        private readonly Dictionary<Junction, Action<Junction.SwitchMode, int>> switchHandlers = new Dictionary<Junction, Action<Junction.SwitchMode, int>>();
        private int nextTrackId, nextJunctionId, uncapturedLinks;
        public string TrackId(RailTrack track) => track != null && trackIds.TryGetValue(track, out var id) ? id : null;
        private IEnumerator BuildTopology()
        {
            var builder = BuildTopologyCore();
            while (true)
            {
                bool next;
                try { next = builder.MoveNext(); }
                catch (Exception e) { Main.Log("TOPOLOGY_CAPTURE_FAILED", e); building = false; RequestRebuild(5); yield break; }
                if (!next) yield break;
                yield return builder.Current;
            }
        }
        private IEnumerator BuildTopologyCore()
        {
            building = true; initialSample = false; scanning = false;
            ResetRouteTopology();
            DetachJunctions();
            var registry = RailTrackRegistry.Instance;
            if (registry == null || registry.TrackRootParent == null) { building = false; RequestRebuild(2); yield break; }
            tracks = registry.TrackRootParent.GetComponentsInChildren<RailTrack>();
            var junctions = registry.TrackRootParent.GetComponentsInChildren<Junction>(); worldJunctions = junctions;
            // IDs are host/session identifiers, not array indices or coincident geometry.
            for (int i = 0; i < tracks.Length; i++) { if (!trackIds.ContainsKey(tracks[i])) trackIds.Add(tracks[i], "t" + (++nextTrackId)); if (i % 128 == 127) yield return null; }
            var liveTracks = new HashSet<RailTrack>(tracks); var old = new List<RailTrack>();
            foreach (var t in trackIds.Keys) if (!liveTracks.Contains(t)) old.Add(t);
            foreach (var t in old) trackIds.Remove(t);
            var liveJunctions = new HashSet<Junction>(junctions); var obsoleteJunctions = new List<Junction>();
            foreach (var j in junctionIds.Keys) if (!liveJunctions.Contains(j)) obsoleteJunctions.Add(j);
            foreach (var j in obsoleteJunctions) junctionIds.Remove(j);
            junctionById.Clear();
            foreach (var j in junctions) { if (!junctionIds.ContainsKey(j)) junctionIds.Add(j, "j" + (++nextJunctionId)); junctionById.Add(junctionIds[j], j); }
            var tableDefs = CaptureTableDefinitions();
            var trackDefs = new List<TrackDef>(tracks.Length); var junctionDefs = new List<JunctionDef>(junctions.Length);
            uncapturedLinks = 0;
            // isJunctionTrack only checks inJunction in the game. Reverse-end
            // turnouts and Double Track must retain the same curve detail.
            var turnoutTracks = new HashSet<RailTrack>();
            foreach (var j in junctions)
                foreach (var branch in j.outBranches)
                    if (branch?.track != null) turnoutTracks.Add(branch.track);
            trackOccupancy = new RailTrackBogiesOnTrack[tracks.Length];
            linkSignatures = new int[tracks.Length];
            var missingGeometry = new List<string>();
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i]; if (track == null) continue;
                var set = track.GetKinkedPointSet();
                if (set == null || set.points.Length < 2) { missingGeometry.Add(TrackId(track)); continue; }
                int segments = Math.Min(1023, Math.Max(2, (int)Math.Ceiling(set.span / (turnoutTracks.Contains(track) ? 3 : 16))));
                int count = Math.Min(set.points.Length, segments + 1); var points = new double[count * 2]; var spans = new double[count];
                for (int p = 0; p < count; p++) { int index = (int)((long)p * (set.points.Length - 1) / (count - 1)); spans[p] = set.points[index].span; points[p * 2] = Math.Round(set.points[index].position.x, 2); points[p * 2 + 1] = Math.Round(set.points[index].position.z, 2); }
                trackDefs.Add(new TrackDef { id = TrackId(track), name = track.name, length = set.span, points = points, spans = spans, a = Links(track, true), b = Links(track, false) });
                trackOccupancy[i] = track.GetComponent<RailTrackBogiesOnTrack>();
                linkSignatures[i] = LinkSignature(track);
                if (i % 12 == 11) yield return null;
            }
            var valid = new HashSet<string>(); foreach (var t in trackDefs) valid.Add(t.id);
            int filteredLinks = uncapturedLinks;
            foreach (var t in trackDefs) { int count = t.a.Length + t.b.Length; t.a = Array.FindAll(t.a, l => valid.Contains(l.track)); t.b = Array.FindAll(t.b, l => valid.Contains(l.track)); filteredLinks += count - t.a.Length - t.b.Length; }
            switchStates.Clear(); changedSwitches.Clear();
            foreach (var j in junctions)
            {
                if (j == null) continue;
                var pos = j.position - WorldMover.currentMove; var branches = new string[j.outBranches.Count];
                for (int i = 0; i < branches.Length; i++) branches[i] = TrackId(j.outBranches[i].track);
                junctionDefs.Add(new JunctionDef { id = junctionIds[j], name = string.IsNullOrEmpty(j.junctionData.junctionIdLong) ? j.name : j.junctionData.junctionIdLong, x = pos.x, z = pos.z, incoming = TrackId(j.inBranch?.track), branches = branches });
                Action<Junction.SwitchMode, int> handler = (mode, branch) => ReadSwitch(j); switchHandlers.Add(j, handler); j.Switched += handler; ReadSwitch(j);
            }
            var signIndex = BuildSignIndex(); while (signIndex.MoveNext()) yield return signIndex.Current;
            var stations = CaptureStations();
            var spawnTracks = new HashSet<string>(stations.SelectMany(s => s.spawnTracks ?? new string[0]), StringComparer.Ordinal);
            foreach (var track in trackDefs)
                if (spawnTracks.Contains(track.id)) track.routePenalty = 180;
            topology = new Topology { epoch = epoch, revision = ++topologyRevision, tracks = trackDefs.ToArray(), junctions = junctionDefs.ToArray(), stations = stations, turntables = tableDefs,
                capture = new TopologyCaptureInfo { registeredTracks = tracks.Length, exportedTracks = trackDefs.Count, filteredLinks = filteredLinks, missingGeometry = missingGeometry.ToArray() } };
            executionGraph = new TrackGraph(topology);
            ClearChanges(); lastJobs = new JobState[0]; lastLocations = new StationDef[0]; nextJobs = 0;
            carStates.Clear(); occupancyStates.Clear(); signalStates.Clear(); blockStates.Clear(); signals?.Reset(); SeedCars();
            building = false; nextCycle = nextPublish = 0;
            Replay();
        }
        private Link[] Links(RailTrack track, bool first)
        {
            var branches = first ? track.GetAllInBranches() : track.GetAllOutBranches(); if (branches == null) return new Link[0];
            var links = new List<Link>(); Junction j = first ? track.inJunction : track.outJunction;
            foreach (var b in branches)
            {
                string id = TrackId(b.track);
                if (id == null) { if (b.track != null) uncapturedLinks++; continue; }
                int gate = -1; string jid = null;
                if (j != null && junctionIds.TryGetValue(j, out jid))
                {
                    bool fromIncoming = j.inBranch?.track == track && j.inBranch.first == first;
                    for (int k = 0; k < j.outBranches.Count; k++)
                    {
                        var output = j.outBranches[k];
                        if (output != null && output.track == (fromIncoming ? b.track : track)
                            && output.first == (fromIncoming ? b.first : first)) { gate = k; break; }
                    }
                    if (gate < 0) jid = null;
                }
                links.Add(new Link { track = id, end = b.first ? 0 : 1, junction = jid, branch = gate });
            }
            return links.ToArray();
        }
        private void ReadSwitch(Junction j)
        {
            if (j == null || !junctionIds.TryGetValue(j, out var id)) return;
            if (switchStates.TryGetValue(id, out var before) && before.branch == j.selectedBranch) return;
            var value = new SwitchState { id = id, branch = j.selectedBranch, revision = ++entityRevision, sampledAt = Protocol.Now };
            switchStates[id] = value; changedSwitches.Add(value);
            var owner=routeLocks.SwitchOwner(id);
            if(owner!=null && watchedRoutes.TryGetValue(owner,out var route) && route.plan.switches.Any(s=>s.id==id&&s.branch!=value.branch))ReleaseWatchedRoute(route,"active","SWITCH_MISALIGNED");
        }
        private void DetachJunctions() { foreach (var item in switchHandlers) if (item.Key != null) item.Key.Switched -= item.Value; switchHandlers.Clear(); }
        private void SampleOccupancy(int index)
        {
            var track = tracks[index]; if (track == null) { RequestRebuild(); return; }
            if (auditTopology && LinkSignature(track) != linkSignatures[index]) RequestRebuild();
            var component = trackOccupancy[index];
            if (component == null) trackOccupancy[index] = component = track.GetComponent<RailTrackBogiesOnTrack>();
            bool occupied = component != null && component.bogiesOnTrack.Count > 0;
            var owners = component?.bogiesOnTrack?.Select(b => b == null ? null : b.Car?.CarGUID).ToArray() ?? new string[0];
            bool ownersKnown = component != null && owners.Length == component.bogiesOnTrack.Count && owners.All(x => !string.IsNullOrEmpty(x));
            string id = TrackId(track);
            if (id != null && (!occupancyStates.TryGetValue(id, out var previous) || previous.occupied != occupied || previous.carsKnown != ownersKnown || !previous.cars.SequenceEqual(owners) || Protocol.Now - previous.sampledAt > 3000))
            {
                var value = new OccupancyState { id = id, occupied = occupied, cars = ownersKnown ? owners.Distinct().ToArray() : new string[0], carsKnown = ownersKnown, sampledAt = Protocol.Now }; occupancyStates[id] = value; changedOccupancy.Add(value);
            }
        }
        private int LinkSignature(RailTrack track)
        {
            if (tableTracks.Contains(track)) return 0;
            int BranchHash(Junction.Branch b) => b == null || tableTracks.Contains(b.track) ? 0 : ((b.track == null ? 0 : b.track.GetHashCode()) * 31 + (b.first ? 1 : 0));
            unchecked
            {
                int hash = BranchHash(track.inBranch) * 31 + BranchHash(track.outBranch);
                int AddJunction(int value, Junction j) {
                    if (j == null) return value;
                    value = value * 31 + j.GetHashCode(); value = value * 31 + BranchHash(j.inBranch);
                    foreach (var branch in j.outBranches) value = value * 31 + BranchHash(branch);
                    return value;
                }
                hash = AddJunction(hash, track.inJunction); hash = AddJunction(hash, track.outJunction);
                return hash;
            }
        }
    }
}
