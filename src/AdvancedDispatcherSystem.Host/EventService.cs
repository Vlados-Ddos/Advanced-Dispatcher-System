using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private string EventTargetKind(string code, string target)
    {
        if (string.IsNullOrEmpty(target)) return null;
        if (code == "blockChanged" || code == "trainEnteredBlock" || code == "trainLeftBlock") return "blocks";
        if (cars.ContainsKey(target)) return "cars";
        if (signals.ContainsKey(target)) return "signals";
        if (blocks.ContainsKey(target)) return "blocks";
        if (switches.ContainsKey(target)) return "switches";
        if (turntables.ContainsKey(target)) return "turntables";
        if (routes.ContainsKey(target)) return "routes";
        if (jobs.ContainsKey(target) || code == "jobInProgress" || code == "jobCompleted" || code == "jobAbandoned" || code == "jobExpired") return "jobs";
        if (players.ContainsKey(target) || code == "playerConnected") return "players";
        if (Graph?.Tracks.ContainsKey(target) == true) return "tracks";
        return null;
    }
    private long eventId;
    private readonly Dictionary<string, string[]> blockMembers = new();
    private readonly Dictionary<string, (bool Origin, bool Destination)> jobLocations = new();
    private Dictionary<string, HashSet<string>> stationTracks = new();
    private void ObserveJobLocations(bool moved)
    {
        foreach (var id in jobLocations.Keys.Where(id => !jobs.TryGetValue(id, out var job) || !job.active).ToArray()) jobLocations.Remove(id);
        foreach (var job in jobs.Values)
        {
            if (!job.active || job.cars.Length == 0 || job.origin == null || job.destination == null || !stationTracks.TryGetValue(job.origin, out var origin) || !stationTracks.TryGetValue(job.destination, out var destination)) continue;
            bool atOrigin = false, atDestination = false, known = true;
            foreach (string id in job.cars)
            {
                if (!cars.ContainsKey(id) || !footprints.TryGetValue(id, out var tracks)) { known = false; break; }
                atOrigin |= tracks.Any(origin.Contains); atDestination |= tracks.Any(destination.Contains);
            }
            if (!known) { jobLocations.Remove(job.id); continue; }
            if (moved && jobLocations.TryGetValue(job.id, out var before))
            {
                if (before.Origin && !atOrigin) Log("jobLeftOrigin", null, job.id, job.origin, "job");
                if (!before.Destination && atDestination) Log("jobEnteredDestination", null, job.id, job.destination, "job");
            }
            jobLocations[job.id] = (atOrigin, atDestination);
        }
    }
    private void ObserveChanges(GameBatch batch)
    {
        if (batch.events != null) foreach (var e in batch.events.Take(128)) Log(e.code, e.actor, e.target, e.detail, e.type ?? "system", e.severity ?? "info", e.source ?? "game", e.time, e.actorId, e.targetKind);
        if (batch.reset) return; // A resync is not a series of gameplay transitions.
        if (batch.switches != null) foreach (var s in batch.switches)
            if (switches.TryGetValue(s.id, out var before) && before.branch != s.branch) Log("switchChanged", null, s.id, (s.branch + 1).ToString(), "switch");
        if (batch.signals != null) foreach (var s in batch.signals)
            if (signals.TryGetValue(s.id, out var before))
            {
                if (s.aspect != before.aspect || s.mode != before.mode || s.off != before.off) Log("signalChanged", null, s.id, s.aspect + " · " + s.mode, "signal", source: "DV Signals");
                if (s.reserved != before.reserved) Log(s.reserved ? "reservationMade" : "reservationReleased", null, s.id, s.block, "reservation", source: "DV Signals");
            }
        if (batch.blocks != null) foreach (var b in batch.blocks)
            if (blocks.TryGetValue(b.id, out var before) && (before.occupied != b.occupied || before.quality != b.quality)) Log("blockChanged", null, b.id, b.quality == "ready" ? b.occupied ? "occupied" : "free" : b.quality, "block", source: b.source);
        if (batch.replacePlayers && players.Count > 0)
        {
            var next = (batch.players ?? Array.Empty<PlayerState>()).Select(p => p.id).ToHashSet();
            foreach (var p in players.Values) if (!next.Contains(p.id)) Log("playerDisconnected", p.name, p.id, "", "player");
        }
        if (batch.players != null) foreach (var p in batch.players) if (!players.ContainsKey(p.id)) Log("playerConnected", p.name, p.id, "", "player");
        if (batch.jobs != null) foreach (var job in batch.jobs)
        {
            if (!jobs.TryGetValue(job.id, out var old)) continue;
            for (int i = 0; i < Math.Min(old.legs.Length, job.legs.Length); i++)
            {
                var leg = job.legs[i];
                if (old.legs[i].state != "Done" && leg.state == "Done")
                    Log(leg.type == "Warehouse" ? "cargoTaskDone" : leg.type == "Transport" ? "transportTaskDone" : "jobTaskDone", job.owner, job.id, leg.to, "job");
            }
        }
    }
    private void ObserveBlockMembership(GameBatch batch)
    {
        if (batch.blocks == null) return;
        foreach (var block in batch.blocks)
        {
            if (!batch.reset && blockMembers.TryGetValue(block.id, out var before))
            {
                foreach (var id in block.trains.Except(before)) Log("trainEnteredBlock", null, block.id, id, "train", source: block.source);
                foreach (var id in before.Except(block.trains)) Log("trainLeftBlock", null, block.id, id, "train", source: block.source);
            }
            blockMembers[block.id] = block.trains;
        }
    }
}
