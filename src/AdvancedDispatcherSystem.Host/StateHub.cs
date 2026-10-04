using AdvancedDispatcherSystem.Core;
using System.Threading.Channels;

namespace AdvancedDispatcherSystem.Host;

public sealed class BrowserPeer : IDisposable
{
    public readonly Channel<byte[]> Outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public readonly CancellationTokenSource Cancel = new();
    private long pending;
    public bool Send(byte[] bytes)
    {
        if (Cancel.IsCancellationRequested) return false;
        if (Interlocked.Add(ref pending, bytes.Length) > 24 * 1024 * 1024 || !Outbox.Writer.TryWrite(bytes)) { Cancel.Cancel(); return false; }
        return true;
    }
    public void Sent(int length) => Interlocked.Add(ref pending, -length);
    public void Dispose() { Cancel.Cancel(); Outbox.Writer.TryComplete(); }
}

public sealed partial class StateHub
{
    private readonly Dictionary<string, string[]> nativeTrackCars = new();
    private readonly string routeStorePath;
    private bool persistedAwaitingReplay;
    public StateHub(string persistenceDirectory=null)
    {
        if(!string.IsNullOrWhiteSpace(persistenceDirectory))
        {
            Directory.CreateDirectory(persistenceDirectory);
            routeStorePath=Path.Combine(persistenceDirectory,"routes.json");
            LoadRoutes();LoadAssignments(persistenceDirectory);
        }
    }
    private void LoadRoutes()
    {
        try
        {
            if(!File.Exists(routeStorePath))return;
            if(new FileInfo(routeStorePath).Length > Protocol.MaxFrameBytes)throw new InvalidDataException("ROUTE_STORE_SIZE");
            var loaded=Json.Read<RoutePlan[]>(File.ReadAllBytes(routeStorePath));
            if(loaded==null || loaded.Length>300)throw new InvalidDataException("ROUTE_STORE_SCHEMA");
            bool damaged=false;
            foreach(var route in loaded) {
                if(!ValidStoredRoute(route)||routes.ContainsKey(route.id)) {damaged=true;continue;}
                routes.Add(route.id,route);
            }
            if(damaged) {
                PreserveDamagedRouteStore();
                Console.Error.WriteLine("ROUTE_STORE_RECORD_REJECTED");
            }
            persistedAwaitingReplay=routes.Values.Any(r=>!Ended(r));
        }
        catch(Exception e) {
            Console.Error.WriteLine("ROUTE_STORE_LOAD_FAILED "+e.GetType().Name);
            if(e is System.Text.Json.JsonException || e is InvalidDataException)
                PreserveDamagedRouteStore();
        }
    }
    private void PreserveDamagedRouteStore()
    {
        try {File.Copy(routeStorePath,routeStorePath+".invalid-"+Guid.NewGuid().ToString("N"));}
        catch(Exception e) when(e is IOException || e is UnauthorizedAccessException) {
            Console.Error.WriteLine("ROUTE_STORE_BACKUP_FAILED "+e.GetType().Name);
        }
    }
    private bool SaveRoutes()
    {
        if(routeStorePath==null)return true;
        try
        {
            var temp=routeStorePath+".tmp";
            // History retention must never evict an older active reservation.
            var retained=routes.Values.Where(r=>!Ended(r)).Concat(routes.Values.Where(Ended).OrderByDescending(r=>r.endedAt).Take(200)).ToArray();
            var bytes=Json.Bytes(retained);
            if(bytes.Length>Protocol.MaxFrameBytes)throw new InvalidDataException("ROUTE_STORE_SIZE");
            File.WriteAllBytes(temp,bytes);
            File.Move(temp,routeStorePath,true);
            return true;
        }
        catch(Exception e) { Console.Error.WriteLine("ROUTE_STORE_SAVE_FAILED "+e.GetType().Name);return false; }
    }
    private readonly object sync = new();
    public readonly string ServerEpoch = Guid.NewGuid().ToString("N");
    private long seq;
    private readonly List<BrowserPeer> peers = new();
    private readonly Queue<byte[]> journal = new();
    private long journalBytes, firstSeq = 1;
    private byte[] cachedSnapshot;
    private long snapshotSeq = -1;
    public Topology Topology { get; private set; }
    public TrackGraph Graph { get; private set; }
    public CapabilityState Capabilities { get; private set; } = new();
    private readonly Dictionary<string, SwitchState> switches = new();
    private readonly Dictionary<string, TurntableState> turntables = new();
    private readonly Dictionary<string, CarState> cars = new();
    private readonly Dictionary<string, PlayerState> players = new();
    private readonly Dictionary<string, SignalState> signals = new();
    private readonly Dictionary<string, BlockState> blocks = new();
    private readonly Dictionary<string, OccupancyState> occupancy = new(), nativeOccupancy = new();
    private readonly Dictionary<string, string[]> footprints = new();
    private readonly Dictionary<string, int> bodyCounts = new();
    private readonly Dictionary<string, HashSet<string>> trackCars = new();
    private readonly Dictionary<string, JobState> jobs = new();
    private readonly Dictionary<string, StationDef> locations = new();
    private readonly Dictionary<string, SignState> signs = new();
    private readonly Dictionary<string, RoutePlan> routes = new();
    private long routeNetworkRevision;
    private string RouteNetworkKey() => ServerEpoch+":"+Topology?.epoch+":"+Topology?.revision+":"+routeNetworkRevision;
    private readonly Queue<EventEntry> events = new();
    private long lastFrame, receivedMotionRecords;
    public bool GameConnected { get; set; }
    public int ClientCount { get { lock (sync) return peers.Count; } }
    public long LastFrameAge => Environment.TickCount64 - Interlocked.Read(ref lastFrame);

    // Route preview is read-only.  Build a deep planning copy while holding
    // the live-state lock for the minimum possible time, then run graph search
    // against that immutable snapshot.  A difficult search therefore cannot
    // stall game Apply(), websocket snapshots, health, or multiplayer state.
    private StateHub PlanningSnapshotLocked()
    {
        var snapshot = new StateHub();
        var topology = Json.Read<Topology>(Json.Bytes(Topology));
        snapshot.SetTopology(topology);
        var batch = new GameBatch {
            epoch = topology.epoch, topologyRevision = topology.revision, reset = true,
            replacePlayers = true, replaceJobs = true, replaceLocations = true, replaceSigns = true,
            switches = Json.Read<SwitchState[]>(Json.Bytes(switches.Values.ToArray())),
            turntables = Json.Read<TurntableState[]>(Json.Bytes(turntables.Values.ToArray())),
            cars = Json.Read<CarState[]>(Json.Bytes(cars.Values.ToArray())),
            players = Json.Read<PlayerState[]>(Json.Bytes(players.Values.ToArray())),
            signals = Json.Read<SignalState[]>(Json.Bytes(signals.Values.ToArray())),
            blocks = Json.Read<BlockState[]>(Json.Bytes(blocks.Values.ToArray())),
            occupancy = Json.Read<OccupancyState[]>(Json.Bytes(occupancy.Values.ToArray())),
            jobs = Json.Read<JobState[]>(Json.Bytes(jobs.Values.ToArray())),
            locations = Json.Read<StationDef[]>(Json.Bytes(locations.Values.ToArray())),
            signs = Json.Read<SignState[]>(Json.Bytes(signs.Values.ToArray())),
            capabilities = Json.Read<CapabilityState>(Json.Bytes(Capabilities)),
        };
        snapshot.Apply(batch);
        foreach (var route in routes.Values)
            snapshot.routes[route.id] = Json.Read<RoutePlan>(Json.Bytes(route));
        snapshot.routeNetworkRevision = routeNetworkRevision;
        snapshot.IndexRouteObjects();
        return snapshot;
    }
    public void SetTopology(Topology topology)
    {
        var graph = new TrackGraph(topology);
        if (string.IsNullOrEmpty(topology.epoch) || topology.revision < 0) throw new ArgumentException("INVALID_TOPOLOGY_ID");
        var nextStationTracks = (topology.stations ?? Array.Empty<StationDef>()).GroupBy(s => s.id).ToDictionary(g => g.Key, g => g.SelectMany(s => s.tracks).ToHashSet());
        lock (sync)
        {
            if (Topology?.epoch == topology.epoch && Topology.revision == topology.revision) return;
            if(Topology!=null&&!persistedAwaitingReplay) foreach(var route in routes.Values.Where(r=>!Ended(r)).ToArray()) {
                if(Topology.epoch!=topology.epoch)EndRoute(route,"interrupted","TOPOLOGY_CHANGED");
                else {route.recalculationKey=null;}
            }
            Topology = topology; Graph = graph; ClearCapturedState();
            stationTracks = nextStationTracks;
            Capabilities = new CapabilityState { mode = Capabilities.mode, language = Capabilities.language, status = "loading", authority = false };
            Publish("topology", topology);
            if(!persistedAwaitingReplay) UpdateRoutes(true);
            Log("topologyChanged", "game", "", topology.tracks.Length.ToString());
        }
    }
    private void ClearCapturedState()
    {
        cars.Clear(); switches.Clear(); turntables.Clear(); players.Clear(); signals.Clear(); blocks.Clear(); occupancy.Clear(); nativeOccupancy.Clear();
        footprints.Clear(); bodyCounts.Clear(); trackCars.Clear(); nativeTrackCars.Clear(); jobs.Clear(); locations.Clear(); signs.Clear(); trackBlocks.Clear(); trackSignals.Clear(); blockMembers.Clear(); jobLocations.Clear();
    }
    public void Apply(GameBatch batch)
    {
        lock (sync)
        {
            if (batch == null) throw new ArgumentException("INVALID_BATCH");
            if (Topology == null) { if (batch.capabilities != null) { Capabilities = batch.capabilities; Publish("capabilities", Capabilities); } return; }
            if (Topology != null && (batch.epoch != Topology.epoch || batch.topologyRevision != Topology.revision)) return;
            ValidateBatch(batch);
            bool routeNetworkChanged = batch.reset ||
                batch.removedCars?.Length>0 || batch.removedSignals?.Length>0 || batch.removedBlocks?.Length>0 ||
                (batch.cars?.Any(c=>!cars.TryGetValue(c.id,out var old)||old.track1!=c.track1||old.track2!=c.track2||old.consist!=c.consist)??false) ||
                (batch.motions?.Any(c=>cars.TryGetValue(c.id,out var old)&&(old.track1!=c.track1||old.track2!=c.track2))??false) ||
                (batch.occupancy?.Any(o=>!nativeOccupancy.TryGetValue(o.id,out var old)||old.occupied!=o.occupied)??false) ||
                (batch.switches?.Any(s=>!switches.TryGetValue(s.id,out var old)||old.branch!=s.branch)??false) ||
                (batch.blocks?.Any(b=>!blocks.TryGetValue(b.id,out var old)||old.occupied!=b.occupied||old.reserved!=b.reserved||NativeBlockKey(old)!=NativeBlockKey(b))??false);
            if(routeNetworkChanged)routeNetworkRevision++;
            Interlocked.Exchange(ref lastFrame, Environment.TickCount64);
            ObserveChanges(batch);
            if (batch.reset) ClearCapturedState();
            ApplyRouteStates(batch.routeStates,batch.reset);
            ApplyTables(batch.turntables);
            var affected = new HashSet<string>(); bool membershipChanged = false;
            if (batch.switches != null) foreach (var v in batch.switches) switches[v.id] = v;
            if (batch.cars != null) foreach (var v in batch.cars)
            {
                if (!cars.TryGetValue(v.id, out var old) || old.consist != v.consist) membershipChanged = true;
                cars[v.id] = v; SetFootprint(v.id, Graph?.Footprint(v) ?? new[] { v.track1, v.track2 }.Where(x => x != null).Distinct().ToArray(), affected);
            }
            if (batch.motions != null) foreach (var motion in batch.motions)
                if (cars.TryGetValue(motion.id, out var old) && motion.sampledAt > old.sampledAt)
                {
                    receivedMotionRecords++; var v = motion.Apply(old); cars[v.id] = v;
                    if (v.track1 != old.track1 || v.track2 != old.track2) SetFootprint(v.id, Graph?.Footprint(v) ?? new[] { v.track1, v.track2 }.Where(x => x != null).Distinct().ToArray(), affected);
                }
            if (batch.removedCars != null) foreach (var id in batch.removedCars) { cars.Remove(id); SetFootprint(id, Array.Empty<string>(), affected); footprints.Remove(id); }
            if (batch.replacePlayers) players.Clear();
            if (batch.players != null) foreach (var v in batch.players) players[v.id] = v;
            if (batch.signals != null) foreach (var v in batch.signals) signals[v.id] = v;
            if (batch.removedSignals != null) foreach (var id in batch.removedSignals) signals.Remove(id);
            if (batch.blocks != null) foreach (var v in batch.blocks) blocks[v.id] = v;
            if (batch.removedBlocks != null) foreach (var id in batch.removedBlocks) { blocks.Remove(id); blockMembers.Remove(id); }
            if (batch.replaceSigns) signs.Clear();
            if (batch.signs != null) foreach (var v in batch.signs) signs[v.id] = v;
            var observedTracks = new HashSet<string>(affected);
            if (batch.occupancy != null) foreach (var v in batch.occupancy) {
                if (!nativeOccupancy.TryGetValue(v.id, out var previous) || previous.occupied != v.occupied) affected.Add(v.id);
                nativeOccupancy[v.id] = v; observedTracks.Add(v.id);
                if (v.carsKnown) nativeTrackCars[v.id] = (v.cars ?? Array.Empty<string>()).Distinct().ToArray(); else nativeTrackCars.Remove(v.id);
            }
            if (observedTracks.Count > 0)
            {
                var updates = new List<OccupancyState>();
                foreach (string id in observedTracks)
                {
                    nativeOccupancy.TryGetValue(id, out var native); bodyCounts.TryGetValue(id, out int count);
                    // The game already limits unchanged native samples. Every received
                    // observation must advance freshness, even just below that interval.
                    // A car-membership update does not prove an old native sample is fresh.
                    var value = new OccupancyState { id = id, occupied = native.occupied || count > 0, cars = native.cars ?? Array.Empty<string>(), carsKnown = native.carsKnown, sampledAt = native.sampledAt };
                    if (!occupancy.TryGetValue(id, out var old) || old.occupied != value.occupied || old.sampledAt != value.sampledAt) { occupancy[id] = value; updates.Add(value); }
                }
                batch.occupancy = updates.ToArray();
            }
            if (batch.replaceLocations) locations.Clear();
            if (batch.locations != null) foreach (var v in batch.locations) locations[v.id] = v;
            if (batch.replaceJobs) jobs.Clear();
            if (batch.jobs != null) {batch.jobs=batch.jobs.Select(WithAssignment).ToArray();foreach (var v in batch.jobs) jobs[v.id] = v;}
            if (batch.capabilities != null) Capabilities = batch.capabilities;
            DeriveBlocks(batch, affected, membershipChanged);
            ObserveBlockMembership(batch);
            if (affected.Count > 0 || batch.jobs != null) ObserveJobLocations(!batch.reset && affected.Count > 0);
            if (batch.blocks?.Length > 0 || batch.signals?.Length > 0 || batch.removedBlocks?.Length > 0 || batch.removedSignals?.Length > 0 || batch.reset) IndexRouteObjects();
            Publish("delta", batch);
            UpdateRoutes(routeNetworkChanged || batch.removedSignals?.Length > 0 || batch.removedBlocks?.Length > 0 || batch.reset || batch.routeStates!=null || batch.switches?.Length > 0 || batch.signals?.Length > 0 || batch.blocks?.Length > 0);
        }
    }
    private void DeriveBlocks(GameBatch batch, HashSet<string> affected, bool membershipChanged)
    {
        var updates = new Dictionary<string, BlockState>();
        if (batch.blocks != null) foreach (var b in batch.blocks) updates[b.id] = b;
        if (Capabilities.signals || blocks.Values.Any(b => b.source != "dispatch"))
        {
            var removed = new List<string>(batch.removedBlocks ?? Array.Empty<string>());
            foreach (var b in blocks.Values.Where(b => b.source == "dispatch").ToArray()) { blocks.Remove(b.id); updates.Remove(b.id); blockMembers.Remove(b.id); removed.Add(b.id); }
            if (removed.Count > 0) batch.removedBlocks = removed.ToArray();
        }
        else if (Graph != null)
        {
            IEnumerable<string> ids = blocks.Count == 0 ? Graph.Tracks.Keys : affected;
            foreach (string id in ids)
            {
                if (!Graph.Tracks.TryGetValue(id, out var track)) continue;
                bool known = occupancy.TryGetValue(id, out var o); string key = "section:" + id;
                if (blocks.TryGetValue(key, out var previous) && previous.occupied == o.occupied && previous.quality == (known ? "ready" : "unknown")) continue;
                var b = new BlockState { id = key, name = track.name, source = "dispatch", tracks = new[] { id }, directions = new[] { 0 }, length = track.length, occupied = o.occupied, quality = known ? "ready" : "unknown", sampledAt = Protocol.Now };
                blocks[key] = b; updates[key] = b;
            }
        }
        if (affected.Count > 0 || membershipChanged || updates.Count > 0)
            foreach (var b in blocks.Values)
            {
                var trainIds = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var id in b.tracks.Concat(b.extraTracks))
                    if (trackCars.TryGetValue(id, out var members)) foreach (var carId in members)
                        if (cars.TryGetValue(carId, out var car) && car.consist != null) trainIds.Add(car.consist);
                if (b.trains.SequenceEqual(trainIds)) continue;
                updates[b.id] = new BlockState { id = b.id, name = b.name, source = b.source, signal = b.signal, tracks = b.tracks, extraTracks = b.extraTracks, directions = b.directions, length = b.length, occupied = b.occupied, reserved = b.reserved, deadEnd = b.deadEnd, quality = b.quality, sampledAt = b.sampledAt, trains = trainIds.ToArray() };
            }
        foreach (var b in updates.Values) blocks[b.id] = b;
        batch.blocks = updates.Count > 0 ? updates.Values.ToArray() : null;
    }
    private void SetFootprint(string id, string[] next, HashSet<string> affected)
    {
        if (footprints.TryGetValue(id, out var before))
        {
            if (before.SequenceEqual(next)) return;
            foreach (string t in before) { bodyCounts[t]--; trackCars[t].Remove(id); affected.Add(t); }
        }
        footprints[id] = next;
        foreach (string t in next) { bodyCounts.TryGetValue(t, out int count); bodyCounts[t] = count + 1; if (!trackCars.TryGetValue(t, out var members)) trackCars[t] = members = new HashSet<string>(); members.Add(id); affected.Add(t); }
    }
    public void Disconnected()
    {
        lock (sync) { GameConnected = false; Capabilities = new CapabilityState { status = "disconnected", mode = Capabilities.mode, language = Capabilities.language }; Publish("capabilities", Capabilities); }
    }
    private void Publish(string type, object payload)
    {
        byte[] bytes = WebEncoding.Encode(new { protocol = Protocol.Version, serverEpoch = ServerEpoch, seq = (++seq).ToString(), type, payload, time = Protocol.Now });
        journal.Enqueue(bytes); journalBytes += bytes.Length;
        while (journal.Count > 256 || journalBytes > 8 * 1024 * 1024) { journalBytes -= journal.Dequeue().Length; firstSeq++; }
        foreach (var p in peers) p.Send(bytes);
    }
    public BrowserPeer Connect(string epoch, long lastSeq)
    {
        lock (sync)
        {
            if (peers.Count >= 50) return null;
            var p = new BrowserPeer(); peers.Add(p);
            if (epoch == ServerEpoch && lastSeq > 0 && lastSeq >= firstSeq - 1 && lastSeq <= seq && seq - lastSeq <= 48)
            {
                long n = firstSeq; foreach (var entry in journal) if (n++ > lastSeq) p.Send(entry);
                p.Send(Json.Bytes(new { type = "resumed", serverEpoch = ServerEpoch, seq = seq.ToString() }));
            }
            else
            {
                if (snapshotSeq != seq)
                {
                    cachedSnapshot = WebEncoding.Encode(new { protocol = Protocol.Version, type = "snapshot", serverEpoch = ServerEpoch, seq = seq.ToString(), payload = new { topology = Topology, switches = switches.Values, turntables = turntables.Values, cars = cars.Values, signals = signals.Values, blocks = blocks.Values, occupancy = occupancy.Values, players = players.Values, jobs = jobs.Values, locations = locations.Values, signs = signs.Values, routes = Topology==null?Array.Empty<RoutePlan>():routes.Values.ToArray(), events = events.ToArray(), capabilities = Capabilities }, time = Protocol.Now }); snapshotSeq = seq;
                }
                p.Send(cachedSnapshot);
            }
            return p;
        }
    }
    public void Remove(BrowserPeer peer) { lock (sync) peers.Remove(peer); peer.Dispose(); }
    private void Log(string code, string actor, string target, string detail, string type = "system", string severity = "info", string source = "game", long time = 0, string actorId = null, string targetKind = null)
    {
        var e = new EventEntry { id = ServerEpoch + ":" + (++eventId), code = code, actor = actor, actorId = actorId, target = target, targetKind = targetKind ?? EventTargetKind(code, target), detail = detail, type = type, severity = severity, source = source, time = time > 0 ? time : Protocol.Now };
        events.Enqueue(e); while (events.Count > 1000) events.Dequeue(); Publish("event", e);
    }
    public void Receipt(Command command, CommandResult result)
    {
        lock (sync) Log(result.status == "applied" ? command.kind : result.code, command.actor, command.target, result.status, "command", result.status == "applied" ? "info" : "warning", "dispatcher");
    }
    public string Validate(Command c)
    {
        lock (sync)
        {
            if (c == null) return "INVALID_COMMAND";
            if (Topology == null || Capabilities.status != "ready" || LastFrameAge > 5000) return "WORLD_NOT_READY";
            if (!Capabilities.authority) return "HOST_REQUIRED";
            if (c.epoch != Topology.epoch) return "STALE_EPOCH";
            if (c.topologyRevision != Topology.revision) return "TOPOLOGY_CHANGED";
            if (string.IsNullOrEmpty(c.id) || c.id.Length > 80) return "INVALID_COMMAND";
            if(c.kind=="acceptJob"||c.kind=="cancelJob") {
                if(!jobs.TryGetValue(c.target??"",out var job)||job.dataQuality!="ready")return "JOB_TASK_UNAVAILABLE";
                if(c.kind=="cancelJob"&&!job.active)return "JOB_STATE_CHANGED";
                if(c.kind=="acceptJob"&&job.state!="Available"&&!job.active)return "JOB_STATE_CHANGED";
            }
            // Admit only a known single transport movement; native execution
            // rereads task identity, destination and physical coupling again.
            if(c.kind=="planRoute" && !string.IsNullOrEmpty(c.jobId) &&
                JobRouteRules.Shunting(jobs.GetValueOrDefault(c.jobId))) {
                var movementError=JobRouteError(c.jobId,c.taskIndex,c.taskId,c.from,c.to,c.via,train:c.train);
                if(movementError!=null)return movementError;
            }
            if(c.kind=="planRoute" && !string.IsNullOrEmpty(c.jobId) && !string.IsNullOrEmpty(c.taskId) &&
                routes.Values.Any(r=>!Ended(r)&&r.lifecycle!="failed"&&r.jobId==c.jobId&&(r.taskId==c.taskId||JobRouteRules.Passenger(jobs.GetValueOrDefault(c.jobId)))))return "JOB_TASK_ROUTE_EXISTS";
            if(c.kind=="assignRoute"||c.kind=="unassignRoute") {
                if(!routes.TryGetValue(c.target??"",out var assignedRoute)||Ended(assignedRoute))return "NOT_FOUND";
                if(RouteAssignmentBusy(assignedRoute))return "ROUTE_ASSIGNMENT_RESERVED";
                if(c.kind=="assignRoute" && (string.IsNullOrEmpty(c.action)||!players.TryGetValue(c.action,out var player)||string.IsNullOrEmpty(player.identityKey)))return "PLAYER_UNAVAILABLE";
            }
            if(c.kind=="recalculateRoute") {
                if(!routes.TryGetValue(c.target??"",out var recalculating))return "NOT_FOUND";
                Evaluate(recalculating);RefreshRecalculationState(recalculating);
                if(!recalculating.canRecalculate)return "ROUTE_RECALCULATION_NOT_NEEDED";
            }
            if(c.kind=="advanceRouteStage") {
                if(!routes.TryGetValue(c.target??"",out var staged)||Ended(staged))return "NOT_FOUND";
                if(!staged.staged||staged.stageStatus!="available")return "STAGED_NEXT_LEG_UNAVAILABLE";
                if(c.reservationMode!=null&&c.reservationMode!="normal"&&c.reservationMode!="protected"&&c.reservationMode!="none")return "INVALID_RESERVATION";
            }
            if(c.target!=null && routes.TryGetValue(c.target,out var pendingEdit) && pendingEdit.recalculationState=="unconfirmed" &&
                new[]{"applyRoute","reserveRoute","reserveRouteSignals","recalculateRoute","confirmRouteEdit","cancelRouteEdit"}.Contains(c.kind))return "ROUTE_EDIT_UNCONFIRMED";
            if ((c.kind.StartsWith("setSignal", StringComparison.Ordinal) || c.kind == "setShunting" || c.kind == "reserveSignal" || c.kind == "cancelSignalReservation") && !Capabilities.signalCommands) return "CAPABILITY_UNAVAILABLE";
            if (c.kind == "loco" && !Capabilities.locoControls) return "CAPABILITY_UNAVAILABLE";
            if (c.kind == "setTurntable") {
                if (c.target == null || !turntables.TryGetValue(c.target, out var table) || !Graph.Turntables.TryGetValue(c.target, out var def)) return "NOT_FOUND";
                if (c.expectedRevision != table.revision) return "STALE_REVISION";
                if (c.index < 0 || c.index >= def.ends.Length) return "INVALID_VALUE";
                if (Protocol.Now-table.sampledAt>5000) return "TURNTABLE_UNKNOWN";
                if (TurntableRules.RequestAligned(table,def,c)) return null;
                if (!TurntableRules.TryCommandGoal(def,c,table.angle,out _)) return "TURNTABLE_MANEUVER_REQUIRED";
                if (!table.available) return table.reason ?? "TURNTABLE_BUSY";
            }
            if (c.kind == "setSwitch")
            {
                if (c.target == null || !switches.TryGetValue(c.target, out var sw) || !Graph.Junctions.TryGetValue(c.target, out var def)) return "NOT_FOUND";
                if (c.branch < 0 || c.branch > byte.MaxValue || c.branch >= def.branches.Length || def.branches[c.branch] == null) return "INVALID_BRANCH";
                if (c.expectedRevision != sw.revision) return "STALE_REVISION";
                if(routes.Values.Any(r=>!Ended(r)&&r.reservationMode=="protected"&&r.reservationState=="reserved"&&r.switches.Any(s=>s.id==c.target&&s.branch!=c.branch)))return "SWITCH_LOCKED";
            }
            if (c.kind == "setSignalMode" || c.kind == "setSignalAspect" || c.kind == "setShunting" || c.kind == "reserveSignal" || c.kind == "cancelSignalReservation") {
                if (c.target == null || !signals.TryGetValue(c.target, out var signal)) return "NOT_FOUND";
                if (signal.objectKind == "sign") return "SIGNAL_COMMAND_UNAVAILABLE";
                if (c.expectedRevision != signal.revision) return "STALE_REVISION";
            }
            return null;
        }
    }
    public object Health(bool accountsRecovery = false) { lock (sync) return new { name = "Advanced Dispatcher System", version = typeof(StateHub).Assembly.GetName().Version.ToString(3), accountsRecovery, gameConnected = GameConnected, clients = peers.Count, seq = seq.ToString(), stateAgeMs = LastFrameAge, capabilities = Capabilities, tracks = Graph?.Tracks.Count ?? 0, cars = cars.Count, signals = signals.Count, blocks = blocks.Count, signs = signs.Count, journalBytes, receivedMotionRecords }; }
}
