using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using AdvancedDispatcherSystem.Core;
using HarmonyLib;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher : MonoBehaviour
    {
        private string epoch;
        private long topologyRevision, entityRevision;
        private bool world, building, initialSample;
        private float rebuildAt = float.PositiveInfinity, nextPublish, nextPlayers, nextCycle, nextJobs;
        private int carCursor, trackCursor, junctionCursor;
        private float nextTopologyAudit;
        private bool auditTopology;
        private bool scanning;
        private double captureMs;
        private Topology topology;
        private IMultiplayerAdapter multiplayer;
        private ISignalsAdapter signals;
        private string signalsError;
        private bool loggedGameTimeUnavailable;
        private Harmony harmony;
        private readonly Stopwatch watch = new Stopwatch();
        private readonly Dictionary<string, SwitchState> switchStates = new Dictionary<string, SwitchState>();
        private readonly Dictionary<string, CarState> carStates = new Dictionary<string, CarState>();
        private readonly Dictionary<string, SignalState> signalStates = new Dictionary<string, SignalState>();
        private readonly Dictionary<string, BlockState> blockStates = new Dictionary<string, BlockState>();
        private readonly Dictionary<string, OccupancyState> occupancyStates = new Dictionary<string, OccupancyState>();
        private readonly List<SwitchState> changedSwitches = new List<SwitchState>();
        private readonly List<CarState> changedCars = new List<CarState>();
        private List<CarMotion> changedMotions = new List<CarMotion>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<List<CarMotion>> motionPool = new System.Collections.Concurrent.ConcurrentQueue<List<CarMotion>>();
        private readonly List<SignalState> changedSignals = new List<SignalState>();
        private readonly List<BlockState> changedBlocks = new List<BlockState>();
        private readonly List<OccupancyState> changedOccupancy = new List<OccupancyState>();
        private readonly List<string> removedCars = new List<string>(), removedSignals = new List<string>(), removedBlocks = new List<string>();
        private readonly Dictionary<string, CommandResult> completed = new Dictionary<string, CommandResult>();
        private readonly Queue<string> completionOrder = new Queue<string>();
        private CarSpawner spawner;
        public string Status => (world ? Main.L("tracks") + " " + tracks.Length + " · " + Main.L("cars") + " " + carStates.Count + " · " + Main.L("signals") + " " + signalStates.Count + " · " + captureMs.ToString("F2") + " " + Main.L("milliseconds") + " · " + Main.L(multiplayer.Mode) : Main.L("waiting")) + (building ? " · " + Main.L("loading") : "");
        public bool IsMultiplayerClient => multiplayer?.Mode == "client";
        private HostSettingsState effectiveHostSettings;
        private bool ShowUndiscovered => effectiveHostSettings?.showUndiscovered ?? Main.Config.ShowUndiscovered;
        public HostSettingsState CurrentHostSettings
        {
            get
            {
                string mode = multiplayer?.Mode;
                var provider = multiplayer as IHostSettingsProvider;
                if (mode == "client") return effectiveHostSettings = provider?.CurrentHostSettings;
                if (mode != "host" && mode != "singleplayer") return effectiveHostSettings = null;
                var local = new HostSettingsState {
                    readOnly = Main.Config.ReadOnly, showUndiscovered = Main.Config.ShowUndiscovered,
                    adminControls = Main.Config.AdminControls, captureBudgetMs = Main.Config.CaptureBudgetMs,
                    remoteLanAccess = Main.Bridge?.RemoteLanAccess ?? Main.Config.RemoteLanAccess,
                    port = Main.Bridge?.Port ?? Main.Config.Port, publicHost = Main.Bridge?.PublicHost ?? Main.Config.PublicHost ?? ""
                };
                provider?.UpdateHostSettings(local);
                return effectiveHostSettings = local;
            }
        }

        private void Awake()
        {
            multiplayer = new StandaloneAdapter();
            RefreshIntegrations();
            WorldStreamingInit.LoadingFinished += WorldReady;
            HookJobs();
            UnloadWatcher.UnloadRequested += WorldStopped;
            harmony = new Harmony("denis.advanced-dispatcher-system"); harmony.PatchAll(typeof(Dispatcher).Assembly); HookSigns(); HookTopologySources();
            if (WorldStreamingInit.Instance && WorldStreamingInit.IsLoaded) WorldReady();
        }
        private void WorldReady()
        {
            if (world) return;
            world = true; epoch = Guid.NewGuid().ToString("N"); topologyRevision = 0; entityRevision = 0;
            spawner = CarSpawner.Instance;
            if (spawner != null) { spawner.CarSpawned += CarAdded; spawner.CarAboutToBeDeleted += CarRemoved; }
            RequestRebuild(0.5f);
        }
        private void WorldStopped()
        {
            ResetRoutes("STALE_EPOCH"); watchedRoutes.Clear(); executionGraph=null;
            if (world && topology != null) Main.Bridge?.Send(new WireFrame { kind = "state", batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, capabilities = new CapabilityState { status = "unloaded", mode = multiplayer.Mode, language = Main.LanguageCode } } });
            ResetTables("WORLD_NOT_READY");
            world = false; building = false; StopAllCoroutines(); StopJobCapture(); VehiclePresentation.Reset(); WeatherCapture.Reset();
            try { signals?.Reset(); } catch (Exception e) { DropSignals(e); } jobsRunning = false; lastJobs = new JobState[0]; lastLocations = new StationDef[0]; ResetJobHistory(); ResetSigns(); ClearEvents(); carJobs.Clear(); nextJobs = 0;
            if (spawner != null) { spawner.CarSpawned -= CarAdded; spawner.CarAboutToBeDeleted -= CarRemoved; spawner = null; }
            DetachJunctions(); carEntries.Clear(); carList.Clear(); consistMassCache.Clear(); carStates.Clear(); switchStates.Clear(); signalStates.Clear(); blockStates.Clear(); occupancyStates.Clear();
            ClearChanges(); trackIds.Clear(); junctionIds.Clear(); junctionById.Clear(); tracks = new RailTrack[0]; worldJunctions = new Junction[0]; trackOccupancy = new RailTrackBogiesOnTrack[0]; linkSignatures = new int[0]; topology = null; rebuildAt = float.PositiveInfinity;
            completed.Clear(); completionOrder.Clear(); pendingSwitches.Clear();
        }
        private void OnDestroy()
        {
            shuttingDown = true;
            WorldStreamingInit.LoadingFinished -= WorldReady; UnloadWatcher.UnloadRequested -= WorldStopped;
            try { WorldStopped(); }
            finally {
                try { UnhookJobs(); }
                finally { DisposeIntegration(passenger, "PASSENGER_DISPOSE_FAILED"); DisposeIntegration(signals, "SIGNALS_DISPOSE_FAILED"); DisposeIntegration(multiplayer, "MULTIPLAYER_DISPOSE_FAILED"); harmony?.UnpatchAll("denis.advanced-dispatcher-system"); }
            }
        }
        public void RequestRebuild(float delay = 1.5f) { if (world) rebuildAt = Math.Min(rebuildAt, Time.realtimeSinceStartup + delay); }
        private void Update()
        {
            UnityEngine.Profiling.Profiler.BeginSample("ADS.Capture");
            watch.Restart();
            try { CaptureUpdate(); }
            finally { watch.Stop(); captureMs = captureMs * 0.9 + watch.Elapsed.TotalMilliseconds * 0.1; UnityEngine.Profiling.Profiler.EndSample(); }
        }
        private void CaptureUpdate()
        {
            RefreshIntegrations();
            // Resolve once per capture frame; per-car visibility reads the
            // retained snapshot without allocating or invoking MPAPI again.
            _ = CurrentHostSettings;
            PollTableCommands();
            PollRoutes();
            if (!world) {
                if (Time.realtimeSinceStartup >= nextPublish) { nextPublish = Time.realtimeSinceStartup + 1; Main.Bridge?.Send(new WireFrame { kind = "state", batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, capabilities = new CapabilityState { status = "unloaded", mode = multiplayer.Mode, language = Main.LanguageCode, sampledAt = Protocol.Now } } }); }
                return;
            }
            try
            {
                if (!building && Time.realtimeSinceStartup >= rebuildAt) { rebuildAt = float.PositiveInfinity; StartCoroutine(BuildTopology()); }
                if (building || topology == null) return;
                ReplayIfRequested();
                if (Main.Bridge != null && Main.Bridge.TryCommand(out var command)) Execute(command);
                double budget = Math.Max(0.3, Main.Config.CaptureBudgetMs);
                // Advance the existing iterator inside the same frame budget.
                // A Unity coroutine used to run after Update, outside both the
                // budget and capture timing, even when signal/car capture used it all.
                if (watch.Elapsed.TotalMilliseconds < budget * 0.3) StepJobs();
                if (watch.Elapsed.TotalMilliseconds < budget * 0.2) SamplePendingSign();
                if (!scanning && Time.realtimeSinceStartup >= nextCycle) { scanning = true; carCursor = trackCursor = junctionCursor = 0; consistMassCache.Clear(); auditTopology = Time.realtimeSinceStartup >= nextTopologyAudit; }
                while (scanning && watch.Elapsed.TotalMilliseconds < budget * 0.7)
                {
                    if (carCursor < carList.Count) SampleCar(carList[carCursor++]);
                    else if (trackCursor < tracks.Length) SampleOccupancy(trackCursor++);
                    else if (junctionCursor < worldJunctions.Length) ReadSwitch(worldJunctions[junctionCursor++]);
                    else { scanning = false; initialSample = true; nextCycle = Time.realtimeSinceStartup + 0.08f; if (auditTopology) nextTopologyAudit = Time.realtimeSinceStartup + 5; }
                }
                if (signals != null && watch.Elapsed.TotalMilliseconds < budget)
                {
                    try { signals.Tick(TrackId, OnSignal, OnBlock, RemoveSignal, RemoveBlock, Math.Max(0.1, budget - watch.Elapsed.TotalMilliseconds)); }
                    catch (Exception e) { DropSignals(e); }
                }
                if (Time.realtimeSinceStartup >= nextPublish) { nextPublish = Time.realtimeSinceStartup + 0.1f; Publish(); }
            }
            catch (Exception e) { Main.Log("CAPTURE_FAILED", e); nextCycle = Time.realtimeSinceStartup + 1; scanning = false; }
        }
        private CapabilityState Capabilities()
        {
            var capability = new CapabilityState
        {
            language = Main.LanguageCode,
            gameTime = DV.Logic.Job.JobsManager.Instance?.Time ?? 0,
            clockRate = DV.Logic.Job.JobsManager.Instance?.currentJobs.Count > 0 ? Time.timeScale : 0,
            signsStatus = signsStatus,
            passengerStatus = passenger?.Status ?? passengerError ?? "absent", passengerVersion = passengerVersion,
            mode = multiplayer.Mode,
            authority = multiplayer.Authority && !Main.Config.ReadOnly,
            status = building || !initialSample ? "loading" : "ready",
            multiplayerVersion = multiplayer.Version,
            signals = signals != null && signals.Ready,
            signalCommands = signals != null && signals.Ready && multiplayer.Authority && !Main.Config.ReadOnly,
            protectedReservations = multiplayer.Authority && !Main.Config.ReadOnly && multiplayer.ProtectedSwitches,
            signalsStatus = signals != null ? (signals.Ready ? "ready" : "loading") : signalsError ?? "absent",
            locoControls = multiplayer.Authority && !Main.Config.ReadOnly && Main.Config.AdminControls,
            trackCount = tracks.Length,
            carCount = carStates.Count,
            signalCount = System.Linq.Enumerable.Count(signalStates.Values, s => s.objectKind != "sign"),
            captureMs = captureMs,
            hostSettings = CurrentHostSettings,
            weather = WeatherCapture.Read(),
            sampledAt = Protocol.Now
        };
            // JobsManager.Time is an elapsed job timer, not the current in-game
            // clock. Read the same native WorldClockController used by the
            // game's clocks and job start-date capture, preserving an explicit
            // unknown state while the world is still loading.
            try
            {
                var clock = DV.TimeKeeping.WorldClockController.Instance;
                if (clock != null)
                {
                    var time = clock.GetCurrentAnglesAndTimeOfDay();
                    if (time.validTime)
                    {
                        capability.gameTimeOfDay = time.timeOfDay.Hour + time.timeOfDay.Minute / 60d;
                        capability.gameTimeOfDayKnown = true;
                        loggedGameTimeUnavailable = false;
                    }
                }
            }
            catch (Exception e)
            {
                if (!loggedGameTimeUnavailable)
                {
                    loggedGameTimeUnavailable = true;
                    Main.Log("GAME_TIME_CAPTURE_UNAVAILABLE", e);
                }
            }
            return capability;
        }
        private void Publish()
        {
            bool playerDue = Time.realtimeSinceStartup >= nextPlayers;
            if (playerDue) nextPlayers = Time.realtimeSinceStartup + 0.2f;
            var motionBuffer = changedMotions;
            var batch = new GameBatch
            {
                epoch = epoch,
                topologyRevision = topologyRevision,
                events = TakeEvents(),
                switches = changedSwitches.ToArray(),
                turntables = CaptureTables(),
                cars = changedCars.ToArray(),
                signals = changedSignals.ToArray(),
                blocks = changedBlocks.ToArray(),
                occupancy = changedOccupancy.ToArray(),
                motions = motionBuffer,
                signs = changedSigns.ToArray(),
                removedCars = removedCars.ToArray(),
                removedSignals = removedSignals.ToArray(),
                removedBlocks = removedBlocks.ToArray(),
                players = playerDue ? CapturePlayers() : null,
                replacePlayers = playerDue,
                capabilities = Capabilities()
            };
            // Buffer ownership transfers to the IPC worker until serialization completes.
            Main.Bridge.Send(new WireFrame { kind = "state", batch = batch }, () => { motionBuffer.Clear(); motionPool.Enqueue(motionBuffer); });
            if (!motionPool.TryDequeue(out changedMotions)) changedMotions = new List<CarMotion>(motionBuffer.Capacity);
            ClearChanges();
        }
        private void ClearChanges() { changedSigns.Clear(); changedCars.Clear(); changedMotions.Clear(); changedSwitches.Clear(); changedSignals.Clear(); changedBlocks.Clear(); changedOccupancy.Clear(); removedCars.Clear(); removedSignals.Clear(); removedBlocks.Clear(); }
        private void OnSignal(SignalState value) { removedSignals.Remove(value.id); signalStates[value.id] = value; changedSignals.Add(value); }
        private void OnBlock(BlockState value) { removedBlocks.Remove(value.id); blockStates[value.id] = value; changedBlocks.Add(value); }
        private void RemoveSignal(string id) { signalStates.Remove(id); removedSignals.Add(id); }
        private void RemoveBlock(string id) { blockStates.Remove(id); removedBlocks.Add(id); }
        private void Result(CommandResult result)
        {
            if (result.status != "accepted")
            {
                if (!completed.ContainsKey(result.id)) completionOrder.Enqueue(result.id);
                completed[result.id] = result;
                while (completionOrder.Count > 4096) { string id = completionOrder.Dequeue(); completed.Remove(id); }
            }
            Main.Bridge?.Send(new WireFrame { kind = "result", result = result });
        }
        private void Execute(Command c)
        {
            CommandResult Error(string code) => new CommandResult { id = c.id, target = c.target, status = "rejected", code = code };
            if (c == null || string.IsNullOrEmpty(c.id)) { Main.Log("INVALID_COMMAND", new ArgumentException("Missing command identity")); return; }
            if (c.epoch != epoch || c.topologyRevision != topologyRevision) { Result(Error("STALE_EPOCH")); return; }
            if (!multiplayer.Authority || Main.Config.ReadOnly) { Result(Error("FORBIDDEN")); return; }
            if (c.deadline < Protocol.Now) { Result(Error("COMMAND_EXPIRED")); return; }
            if(c.routeId!=null&&watchedRoutes.TryGetValue(c.routeId,out var endedRoute)&&endedRoute.state.lifecycle!="active") {Result(Error("ROUTE_ENDED"));return;}
            if (completed.TryGetValue(c.id, out var done)) { Main.Bridge.Send(new WireFrame { kind = "result", result = done }); return; }
            try
            {
                if(TryJobCommand(c))return;
                if(TryRouteCommand(c))return;
                if (c.kind == "setSwitch")
                {
                    if (!junctionById.TryGetValue(c.target ?? "", out var j) || j == null) { Result(Error("NOT_FOUND")); return; }
                    if (!switchStates.TryGetValue(c.target, out var state)) { ReadSwitch(j); Result(Error("STALE_REVISION")); return; }
                    if (state.branch != j.selectedBranch || c.expectedRevision != state.revision) { ReadSwitch(j); Result(Error("STALE_REVISION")); return; }
                    if (c.branch < 0 || c.branch > byte.MaxValue || c.branch >= j.outBranches.Count) { Result(Error("INVALID_BRANCH")); return; }
                    if (!AllowsProtectedSwitch(j,c.branch,c.routeId)) { Result(Error("SWITCH_LOCKED")); return; }
                    string unsafeSwitch = j.selectedBranch == c.branch ? null : SwitchSafety(j);
                    if (unsafeSwitch != null) { Result(Error(unsafeSwitch)); return; }
                    RememberRouteSwitch(c, j);
                    // Route alignment is a dispatcher operation. FORCED is the
                    // native mode used by DoubleTrack's remote dispatch hook;
                    // safety and protected-lock checks have already run above.
                    if (j.selectedBranch != c.branch) j.Switch(Junction.SwitchMode.FORCED, (byte)c.branch);
                    ReadSwitch(j);
                    Main.Bridge?.Send(new WireFrame { kind = "state", batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, switches = new[] { switchStates[c.target] } } });
                    Result(new CommandResult { id = c.id, target = c.target, status = j.selectedBranch == c.branch ? "applied" : "rejected", code = j.selectedBranch == c.branch ? null : "SWITCH_CHANGED_EXTERNALLY", revision = switchStates[c.target].revision });
                }
                else if (c.kind == "setTurntable") {var reserved=ReservedTable(c.target,c);if(reserved!=null)Result(Error(reserved));else TurntableCommand(c);}
                else if (c.kind == "loco") Result(c.role == "admin" && Main.Config.AdminControls ? LocoCommand(c) : Error("FORBIDDEN"));
                else if (c.kind == "rescan") { RequestRebuild(0); Result(new CommandResult { id = c.id, status = "applied" }); }
                else if (signals != null)
                {
                    var result = signals.Execute(c, Result); if (result != null) Result(result);
                }
                else Result(Error("CAPABILITY_UNAVAILABLE"));
            }
            catch (Exception e) { Main.Log("COMMAND_FAILED", e); Result(Error("COMMAND_FAILED")); }
        }
        private sealed class UnavailableMultiplayer : IMultiplayerAdapter
        {
            public string Version => "unknown"; public string Mode => "multiplayer-unavailable"; public bool Authority => false;
            public bool ProtectedSwitches => false;
            public bool PublishSignalReservation(int signal, bool reserved) => false;
            public PlayerState[] CapturePlayers() => new PlayerState[0]; public void Dispose() { }
        }
        [HarmonyPatch(typeof(RailTrack), "Awake")]
        private static class TrackAwake { private static void Postfix() => Main.Runtime?.RequestRebuild(); }
        [HarmonyPatch(typeof(RailTrack), "OnDestroy")]
        private static class TrackDestroyed { private static void Postfix() => Main.Runtime?.RequestRebuild(); }
        [HarmonyPatch(typeof(RailTrack), "TrackPointsUpdated_Invoke")]
        private static class TrackGeometry { private static void Postfix(RailTrack __instance) { if (Main.Runtime != null && !Main.Runtime.tableTracks.Contains(__instance)) Main.Runtime.RequestRebuild(); } }
    }
}
