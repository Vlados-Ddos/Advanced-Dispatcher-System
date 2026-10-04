using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Game;
using global::Signals.Common;
using global::Signals.Game;
using global::Signals.Game.Aspects;
using global::Signals.Game.Controllers;
using global::Signals.Game.Railway;
using UnityEngine;
using DVSignal = global::Signals.Game.Signal;
using Mp = global::Signals.Game.MultiplayerIntegration;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter : ISignalsAdapter
    {
        private readonly Dictionary<int, DVSignal> registry = new Dictionary<int, DVSignal>();
        private readonly List<DVSignal> list = new List<DVSignal>();
        private readonly Dictionary<int, SignalState> cache = new Dictionary<int, SignalState>();
        private readonly Dictionary<int, BlockState> blockCache = new Dictionary<int, BlockState>();
        private readonly Dictionary<int, TrackBlock> sourceBlocks = new Dictionary<int, TrackBlock>();
        private readonly Dictionary<int, float> remoteRefreshAt = new Dictionary<int, float>();
        private readonly Queue<DVSignal> dirty = new Queue<DVSignal>();
        private readonly HashSet<int> dirtyIds = new HashSet<int>();
        private readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private readonly HashSet<int> uncertainReservations = new HashSet<int>();
        private sealed class Lamps { public string[] colors; public bool[] blinking; }
        private readonly Dictionary<IAspect, Lamps> lampCache = new Dictionary<IAspect, Lamps>();
        internal static bool IsShuntingSignal(bool shuntingHead, SignalType controllerType) => shuntingHead || controllerType == SignalType.Shunting;
        private SignalManager manager;
        private int cursor;
        private long revision;
        private float nextRegistry;
        private bool active;
        private Dictionary<int, DVSignal> registryBuilding;
        private int controllerCursor;
        private Action<string> removeCapturedBlock;
        private struct Occupancy { public bool value; public float until; }
        private readonly Dictionary<RailTrack, Occupancy> occupiedTracks = new Dictionary<RailTrack, Occupancy>();
        private Action<SignalState> sendSignal;
        private Action<BlockState> sendBlock;
        private Func<RailTrack, string> getTrack;
        private sealed class Pending { public Command command; public Action<CommandResult> reply; public bool cancel; }
        public bool Ready => SignalManager.Running && manager != null;
        public Adapter()
        {
            try {
            InstallReservationGuards();
            EnsureSignalSynchronization();
            SignalManager.AspectChanged += Aspect;
            SignalManager.OperationModeChanged += Mode;
            SignalManager.OverrideChanged += Override;
            SignalManager.ShuntingAllowedChanged += Shunting;
            TrackReserver.ReservationMade += ReservationMade;
            TrackReserver.ReservationCleared += ReservationCleared;
            Mp.OnReservationRequestResultReceived += ReservationResult;
            Mp.OnReservationClearResultReceived += CancelResult;
            TrackChecker.OnTrackOccupiedManually += OccupancyDirty; TrackChecker.OnTrackClearedManually += OccupancyDirty;
            UnityEngine.Debug.Log("ADS SIGNAL_RESOLVER_LOADED controller-gates=all native-footprint=canonical-v2");
            }
            catch {Dispose();throw;}
        }
        private void Aspect(DVSignal signal, IAspect aspect) => Dirty(signal);
        private void Mode(DVSignal signal, SignalOperationMode mode) => Dirty(signal);
        private void Override(DVSignal signal, int value) => Dirty(signal);
        private void Shunting(DVSignal signal, bool value) => Dirty(signal);
        private void Dirty(DVSignal signal) { if (active && signal != null && dirtyIds.Add(signal.Id)) dirty.Enqueue(signal); }
        private void OccupancyDirty(RailTrack track) => occupiedTracks.Remove(track);
        public void Reset()
        {
            ReleaseAllOwned();
            uncertainReservations.Clear(); active = false; manager = null; registryBuilding = null; registry.Clear(); list.Clear(); dirty.Clear(); dirtyIds.Clear(); cache.Clear(); blockCache.Clear(); sourceBlocks.Clear(); lampCache.Clear(); occupiedTracks.Clear(); remoteRefreshAt.Clear(); cursor = 0; nextRegistry = 0;
            foreach (var p in pending.Values) p.reply(new CommandResult { id = p.command.id, status = "outcomeUnknown", code = "STALE_EPOCH" }); pending.Clear();
        }
        public void Tick(Func<RailTrack, string> trackId, Action<SignalState> signal, Action<BlockState> block, Action<string> removeSignal, Action<string> removeBlock, double budgetMs)
        {
            EnsureSignalSynchronization();
            active = true; getTrack = trackId; sendSignal = signal; sendBlock = block; removeCapturedBlock = removeBlock;
            var timer = Stopwatch.StartNew();
            if (!SignalManager.Running || SignalManager.Instance == null)
            {
                if (manager != null) { foreach (var id in cache.Keys) removeSignal("s" + id); foreach (var id in blockCache.Keys) removeBlock("b:s" + id); Reset(); list.Clear(); registry.Clear(); manager = null; }
                return;
            }
            if (manager != SignalManager.Instance || registryBuilding == null && Time.realtimeSinceStartup >= nextRegistry)
            {
                if (manager != SignalManager.Instance) {
                    foreach (var id in registry.Keys) { removeSignal("s" + id); removeBlock("b:s" + id); }
                    Reset(); active = true;
                }
                manager = SignalManager.Instance; nextRegistry = Time.realtimeSinceStartup + 3;
                registryBuilding = new Dictionary<int, DVSignal>(); controllerCursor = 0;
            }
            if (registryBuilding != null)
            {
                while (controllerCursor < manager.AllControllers.Count && timer.Elapsed.TotalMilliseconds < budgetMs)
                {
                    var controller = manager.AllControllers[controllerCursor++]; if (controller != null && controller.Exists) foreach (var s in controller.AllSignals) Collect(s, registryBuilding);
                }
                if (controllerCursor < manager.AllControllers.Count) return;
                var found = registryBuilding; registryBuilding = null;
                foreach (int id in registry.Keys) if (!found.ContainsKey(id)) { removeSignal("s" + id); removeBlock("b:s" + id); cache.Remove(id); blockCache.Remove(id); sourceBlocks.Remove(id); }
                if (found.Count != registry.Count || !SameRegistry(found)) lampCache.Clear();
                registry.Clear(); list.Clear(); foreach (var pair in found) { registry.Add(pair.Key, pair.Value); list.Add(pair.Value); }
                if (cursor >= list.Count) cursor = 0;
            }
            int count = 0;
            while (dirty.Count > 0 && count++ < 64 && timer.Elapsed.TotalMilliseconds < budgetMs) { var s = dirty.Dequeue(); dirtyIds.Remove(s.Id); if (registry.TryGetValue(s.Id, out var current) && current == s) Sample(s); }
            count = 0;
            while (list.Count > 0 && count++ < 24 && timer.Elapsed.TotalMilliseconds < budgetMs) { if (cursor >= list.Count) cursor = 0; Sample(list[cursor++]); }
            ExpirePending();
        }
        private bool SameRegistry(Dictionary<int, DVSignal> found)
        {
            foreach (var pair in found) if (!registry.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value)) return false;
            return true;
        }
        private static void Collect(DVSignal signal, Dictionary<int, DVSignal> into)
        {
            if (signal == null || into.ContainsKey(signal.Id)) return;
            into.Add(signal.Id, signal); Collect(signal.DistantSignal, into);
        }
        private void Sample(DVSignal s)
        {
            if (s == null || s.Definition == null || s.Controller == null || !s.Controller.Exists || s.AllAspects == null) return;
            RefreshRemoteController(s.Controller);
            bool reserved = TrackReserver.HasReservation(s, out float remaining);
            var block = s.Block;
            if (block != null)
            {
                BlockState before; blockCache.TryGetValue(s.Id, out before);
                bool changedSource = !sourceBlocks.TryGetValue(s.Id, out var original) || original != block;
                bool occupied = BlockOccupied(block);
                if (changedSource || before == null || before.occupied != occupied || before.reserved != reserved || before.name!=s.Name)
                {
                    string[] tracks, extras; int[] directions;
                    if (changedSource || before == null)
                    {
                        var trackList = new List<string>(); var directionList = new List<int>(); var extraList = new List<string>();
                        foreach (var t in block.Tracks) { string id = getTrack(t.Track); if (id != null) { trackList.Add(id); directionList.Add(t.Direction == TrackDirection.Out ? 1 : -1); } }
                        foreach (var t in block.ExtraTracks) { string id = getTrack(t); if (id != null) extraList.Add(id); }
                        tracks = trackList.ToArray(); directions = directionList.ToArray(); extras = extraList.ToArray();
                    }
                    else { tracks = before.tracks; directions = before.directions; extras = before.extraTracks; }
                    var state = new BlockState { id = "b:s" + s.Id, name = s.Name, source = "DV Signals", signal = "s" + s.Id, tracks = tracks, extraTracks = extras, directions = directions, occupied = occupied, reserved = reserved, length = block.Length, deadEnd = block.IsDeadEnd, sampledAt = Protocol.Now };
                    sourceBlocks[s.Id] = block;
                    // Native UpdateBlocks can recreate an equivalent object. Its
                    // instance ID alone must not repaint/recalculate the Web UI.
                    if(!SignalBlockIdentity.SameCapture(before,state)) {blockCache[s.Id] = state;sendBlock(state);}
                }
            }
            else if (blockCache.Remove(s.Id)) { sourceBlocks.Remove(s.Id); removeCapturedBlock("b:s" + s.Id); }
            cache.TryGetValue(s.Id, out var old);
            var current = s.CurrentAspect;
            var definition = current?.GetDefinition();
            string aspect = current?.Id ?? "off", mode = s.Operation.ToString(), type = s.Controller.Type.ToString();
            bool isShunting = IsShuntingSignal(s.IsShunting, s.Controller.Type), stop = current?.DisallowPassing ?? false;
            bool turntableKnown = s.Controller is TurntableSignalController;
            bool turntableConnected = turntableKnown && ((TurntableSignalController)s.Controller).IsConnected;
            float passingSpeed = definition != null && definition.UsePassingSpeed ? definition.PassingSpeed : -1;
            var pos = s.Definition.transform.position - WorldMover.currentMove; var placement = s.Controller.PlacementInfo;
            string track = placement.HasValue ? getTrack(placement.Value.Track) : null;
            string authorityTrack = getTrack(NativeStartingTrack(s.Controller));
            double yaw = s.Definition.transform.eulerAngles.y;
            var layout = CaptureLamps(s, old?.lampLayout);
            var parts = CaptureParts(s, old?.parts);
            bool sign = IsRailwaySign(s);
            CaptureRouteBinding(s, out bool bindingRequired, out string incoming, out string[] branches, out int incomingDirection, out int[] branchDirections);
            string visualKind = VisualKind(s), visualState = VisualState(s);
            int direction = placement.HasValue ? MovementDirection(placement.Value.Direction) : 0;
            if (old != null && old.authorityTrack == authorityTrack && old.routeIncomingDirection == incomingDirection && old.routeBranchDirections.SequenceEqual(branchDirections) && ReferenceEquals(parts, old.parts) && (old.objectKind == "sign") == sign && ReferenceEquals(layout, old.lampLayout) && old.direction == direction && old.span == (placement?.Span ?? 0) && old.displayOrder == s.Definition.HUDDisplayOrder && old.reservable == (s.AllowReserving && s.Parent == null) && old.name == s.Name && old.aspectIndex == s.CurrentAspectIndex && old.overrideIndex == s.ManualOverrideAspect && old.aspect == aspect && old.mode == mode && old.shunting == s.ShuntingAllowed && old.reserved == reserved && (!reserved || Math.Floor(old.reservationSeconds) == Math.Floor(remaining)) && old.shuntingSignal == isShunting && old.type == type && old.off == s.IsOff && old.stop == stop && old.passingSpeed == passingSpeed && old.track == track && old.routeBindingRequired == bindingRequired && old.routeIncoming == incoming && System.Linq.Enumerable.SequenceEqual(old.routeBranches, branches) && old.visualKind == visualKind && old.visualState == visualState && old.turntableConnected == turntableConnected && old.turntableStateKnown == turntableKnown && Math.Abs(old.x - pos.x) < 0.05 && Math.Abs(old.z - pos.z) < 0.05 && Math.Abs(old.yaw - yaw) < 0.1 && (old.block != null) == (block != null)) return;
            Lamps lamps = null;
            if (current != null && definition != null && !lampCache.TryGetValue(current, out lamps))
            {
                var colors = new List<string>(); var blinking = new List<bool>();
                if (definition.OnLights != null) foreach (var lamp in definition.OnLights) { colors.Add("#" + ColorUtility.ToHtmlStringRGB(lamp.Colour)); blinking.Add(false); }
                if (definition.BlinkingLights != null) foreach (var lamp in definition.BlinkingLights) { colors.Add("#" + ColorUtility.ToHtmlStringRGB(lamp.Colour)); blinking.Add(true); }
                lamps = new Lamps { colors = colors.ToArray(), blinking = blinking.ToArray() }; lampCache[current] = lamps;
            }
            var aspectNames = old?.aspects;
            if (aspectNames == null || aspectNames.Length != s.AllAspects.Length) { aspectNames = new string[s.AllAspects.Length]; for (int i = 0; i < aspectNames.Length; i++) aspectNames[i] = s.AllAspects[i].Id; }
            var value = new SignalState
            {
                routeBindingRequired = bindingRequired, routeIncoming = incoming, routeBranches = branches,
                routeIncomingDirection=incomingDirection,routeBranchDirections=branchDirections,
                displayLayer = sign || visualKind == "discDistant" && (s.Controller.Type == SignalType.Distant || s.Parent != null) || visualKind == "discShunting" && isShunting ? "additionalSigns" : "signals",
                parts = parts, objectKind = sign ? "sign" : "signal", signKind = sign ? SignKind(s) : null,
                lampLayout = layout, controller = s.Controller.Id.ToString(), displayOrder = s.Definition.HUDDisplayOrder,
                reservable = s.AllowReserving && s.Parent == null,
                id = "s" + s.Id,
                name = s.Name,
                aspect = aspect,
                aspectIndex = s.CurrentAspectIndex,
                overrideIndex = s.ManualOverrideAspect,
                aspectCount = s.AllAspects.Length,
                mode = mode,
                type = type,
                shuntingSignal = isShunting, classificationKnown = true,
                turntableConnected = turntableConnected, turntableStateKnown = turntableKnown,
                track = track, authorityTrack = authorityTrack,
                span = placement?.Span ?? 0,
                direction = direction,
                x = pos.x,
                z = pos.z,
                yaw = yaw,
                off = s.IsOff,
                stop = stop,
                shunting = s.ShuntingAllowed,
                reserved = reserved,
                reservationSeconds = remaining,
                block = block == null ? null : "b:s" + s.Id,
                parent = s.Parent == null ? null : "s" + s.Parent.Id,
                passingSpeed = passingSpeed,
                lamps = lamps?.colors ?? new string[0],
                blinkingLamps = lamps?.blinking ?? new bool[0],
                aspects = aspectNames,
                aspectInfo = old?.aspectInfo ?? CaptureAspectInfo(s), visualKind = visualKind, visualState = visualState,
                revision = SameControlState(old,s.CurrentAspectIndex,s.ManualOverrideAspect,mode,s.ShuntingAllowed,reserved) ? old.revision : ++revision,
                sampledAt = Protocol.Now
            };
            cache[s.Id] = value; sendSignal(value);
        }
        private void RefreshRemoteController(BasicSignalController controller)
        {
            if (controller == null || !controller.Exists || !Mp.IsMpRunning) return;
            float now = Time.realtimeSinceStartup;
            if (remoteRefreshAt.TryGetValue(controller.Id, out var next) && now < next) return;
            var distance = controller.GetCameraDistanceSqr();
            // SignalManager itself waits for an active camera. Do not turn a
            // missing camera into an all-controller forced evaluation during
            // loading or scene teardown.
            if (float.IsNaN(distance) || float.IsInfinity(distance) ||
                !SignalRefreshPolicy.NeedsRemoteRefresh(distance, true)) return;
            SignalRefreshPolicy.Refresh(distance, true,
                () => controller.UpdateBlocks(),
                () => { foreach (var head in controller.AllSignals) head?.UpdateAspect(false); });
            // Keep the map live without running the expensive native evaluator
            // on every 24-head capture slice. State changes still arrive through
            // AspectChanged and are sampled immediately via the dirty queue.
            remoteRefreshAt[controller.Id] = now + 0.5f;
        }
        private static SignalAspectInfo[] CaptureAspectInfo(DVSignal signal)
        {
            var result = new SignalAspectInfo[signal.AllAspects.Length];
            for (int i = 0; i < result.Length; i++) {
                var a = signal.AllAspects[i]; var d = a.GetDefinition();
                result[i] = new SignalAspectInfo { id = a.Id, stop = a.DisallowPassing, speed = d != null && d.UsePassingSpeed ? d.PassingSpeed : -1,
                    reason = d is global::Signals.Common.Aspects.TurntableConnectedAspectDefinition turntable && turntable.Invert ? "aspectTableDisconnected" :
                        d is global::Signals.Common.Aspects.SpecialRequireReservationAspectDefinition ? "aspectReservationRequired" :
                        d is global::Signals.Common.Aspects.SpecialMatchingPathAspectDefinition ? "aspectPath" : null };
            }
            return result;
        }
        private static string VisualKind(DVSignal signal)
        {
            var mechanical = MechanicalKind(signal.Definition.OffStateHUDSprite?.name);
            if (mechanical != null) return mechanical;
            if (signal.Controller is DistantSignalController distant && distant.IsRepeater) return "repeater";
            if (signal.Parent != null) return "distant";
            if (signal.Controller.Type == SignalType.Distant) return "distant";
            if (signal.Controller.Type == SignalType.Repeater) return "repeater";
            if (signal.Controller.Type == SignalType.Spacing) return "spacing";
            if (signal.IsShunting || signal.Controller.Type == SignalType.Shunting) return "shunting";
            return "main";
        }
        private IEnumerable<DVSignal> NativeReservationSignals(RoutePlan plan)
        {
            var tracks=new HashSet<string>(plan.tracks);
            return SignalManager.Instance.AllControllers.Where(controller=>controller!=null&&controller.Exists&&
                ResolverTrack(controller)!=null&&tracks.Contains(getTrack(ResolverTrack(controller))))
                .SelectMany(controller=>controller.AllSignals);
        }

        private static RailTrack NativeStartingTrack(BasicSignalController controller)
        {
            if (controller is TrackSignalController track) return track.StartingTrack;
            if (controller is JunctionSignalController junction) return junction.StartingTrack;
            return null;
        }

        private static TrackDirection? NativeDirection(BasicSignalController controller)
        {
            if (controller is TrackSignalController track) return track.Direction;
            if (controller is JunctionSignalController junction) return junction.Direction;
            return null;
        }

        private RailTrack ResolverTrack(BasicSignalController controller)
        {
            var native=NativeStartingTrack(controller);
            if(native!=null) return native;
            return controller?.PlacementInfo?.Track;
        }

        private bool BlockTouchesRoute(DVSignal signal, RoutePlan plan)
        {
            if (signal?.Block?.Tracks == null || plan?.tracks == null) return false;
            var route = new HashSet<string>(plan.tracks);
            return signal.Block.Tracks.Any(info => info != null && route.Contains(getTrack(info.Track)));
        }

        private bool RouteSignalApplies(DVSignal signal, RoutePlan plan)
        {
            return ApplicableRouteIndices(signal, plan).Any();
        }
        private IEnumerable<int> ApplicableRouteIndices(DVSignal signal, RoutePlan plan)
        {
            var placement = signal.Controller.PlacementInfo;
            if (!placement.HasValue || IsRailwaySign(signal) || plan.directions == null ||
                plan.tracks == null || plan.directions.Length != plan.tracks.Length) yield break;
            bool junctionGate = signal.Controller is JunctionSignalController;
            // A TrackBlock is created from the controller's live native
            // StartingTrack/Direction. PlacementInfo describes where the mast
            // is drawn and may point at the approach rail of a branch. Use it
            // only for a JunctionSignalController's incoming gate; otherwise
            // selecting by placement can bind a perfectly valid route to the
            // opposite native block.
            string track = junctionGate ? getTrack(placement.Value.Track) : getTrack(ResolverTrack(signal.Controller));
            CaptureRouteBinding(signal,out var required,out var incoming,out var branches,out var incomingDirection,out var branchDirections);
            var nativeDirection=NativeDirection(signal.Controller);
            var direction = junctionGate ? MovementDirection(placement.Value.Direction) :
                nativeDirection.HasValue ? nativeDirection==TrackDirection.Out ? 1 : nativeDirection==TrackDirection.In ? -1 : 0 : MovementDirection(placement.Value.Direction);
            if (string.IsNullOrEmpty(track) || direction == 0) yield break;
            var snapshot = new SignalState { track=track,span=placement.Value.Span,
                direction=direction,
                routeBindingRequired=required,routeIncoming=incoming,routeBranches=branches,
                routeIncomingDirection=incomingDirection,routeBranchDirections=branchDirections };
            // A loop can contain the same physical rail more than once. The
            // first Array.IndexOf occurrence is not an identity; evaluate each
            // oriented traversal and let the native block sequence select the
            // matching topology phase.
            for (int index = 0; index < plan.tracks.Length; index++) {
                if (plan.tracks[index] != track || plan.directions[index] != direction) continue;
                double origin=index==0?plan.startSpan:plan.directions[index]>0?0:double.MaxValue;
                if (RouteSignalRules.Applies(snapshot,plan,index,origin)) yield return index;
            }
        }
        private bool RouteSignalMatches(DVSignal signal, RoutePlan plan)
        {
            if(!RouteSignalApplies(signal,plan)||signal.Block==null)return false;
            string nativeStart=getTrack(ResolverTrack(signal.Controller));
            var paths=signal.Block.Tracks;
            if(paths==null||paths.Length==0)return false;
            var ids=new string[paths.Length];var directions=new int[paths.Length];
            for(int i=0;i<paths.Length;i++){ids[i]=getTrack(paths[i].Track);directions[i]=paths[i].Direction==TrackDirection.Out?1:-1;}
            foreach (var index in ApplicableRouteIndices(signal, plan)) {
                int blockIndex=index;
                if (plan.tracks[index] != ids[0]) {
                    if (!(signal.Controller is JunctionSignalController) || nativeStart != ids[0]) continue;
                    CaptureRouteBinding(signal,out var required,out var incoming,out _,out _,out _);
                    if (!required || incoming==null) continue;
                    // Junction masts stand on the approach; their native
                    // blocks start on the selected outgoing branch. Bind to
                    // the exit of THIS gate occurrence, never an arbitrary
                    // later Array.IndexOf match on a loop.
                    int gate=index;
                    while(gate<plan.tracks.Length && plan.tracks[gate]!=incoming)gate++;
                    blockIndex=gate+1;
                }
                if (RouteSignalRules.BlockFollows(plan,blockIndex,ids,directions)) return true;
            }
            return false;
        }

        private string RouteSignalMismatchReason(DVSignal signal, RoutePlan plan)
        {
            if (signal == null || signal.Block == null) return "BLOCK_UNAVAILABLE";
            if (!RouteSignalApplies(signal, plan)) return "SIGNAL_NOT_APPLICABLE";
            var primary = signal.Block.Tracks ?? Array.Empty<TrackInfo>();
            if (primary.Length == 0) return "BLOCK_EMPTY";
            var ids = primary.Select(t => t == null ? null : getTrack(t.Track)).ToArray();
            if (ids.Any(string.IsNullOrEmpty)) return "TOPOLOGY_CHANGED";
            var dirs = primary.Select(t => t.Direction == TrackDirection.Out ? 1 : -1).ToArray();
            var route = plan.tracks ?? Array.Empty<string>();
            var routeDirs = plan.directions ?? Array.Empty<int>();
            if (route.Length != routeDirs.Length) return "ROUTE_SNAPSHOT_INVALID";
            string firstMismatch = null;
            for (int i = 0; i < route.Length; i++) {
                if (route[i] != ids[0]) continue;
                bool mismatch = false;
                for (int b = 0; b < ids.Length && i + b < route.Length; b++) {
                    if (ids[b] != route[i + b]) { firstMismatch ??= "SWITCH_BRANCH_MISMATCH"; mismatch = true; break; }
                    if (dirs[b] != routeDirs[i + b]) { firstMismatch ??= "DIRECTION_MISMATCH"; mismatch = true; break; }
                }
                if (mismatch) continue;
                return ids.Length > route.Length - i ? "NATIVE_BLOCK_EXTENDS_AFTER_DESTINATION" : "BLOCK_SEQUENCE_MATCH";
            }
            return firstMismatch ?? "BLOCK_SELECTION_MISMATCH";
        }
        private bool BlockOccupied(TrackBlock block)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var t in block.Tracks) if (TrackOccupied(t.Track, now)) return true;
            foreach (var t in block.ExtraTracks) if (TrackOccupied(t, now)) return true;
            return false;
        }
        private bool TrackOccupied(RailTrack track, float now)
        {
            if (track == null) return false;
            if (!occupiedTracks.TryGetValue(track, out var sample) || now >= sample.until)
            {
                sample = new Occupancy { value = TrackChecker.IsOccupied(track, CrossingCheckMode.IntersectionOnly), until = now + 0.5f }; occupiedTracks[track] = sample;
            }
            return sample.value;
        }
        public void Dispose()
        {
            RemoveSignalSynchronization();
            ReleaseAllOwned(); reservationHarmony?.UnpatchAll("denis.ads.signals-reservations");reservationHarmony=null;guardOwner=null;
            SignalManager.AspectChanged -= Aspect; SignalManager.OperationModeChanged -= Mode; SignalManager.OverrideChanged -= Override; SignalManager.ShuntingAllowedChanged -= Shunting;
            TrackReserver.ReservationMade -= ReservationMade; TrackReserver.ReservationCleared -= ReservationCleared;
            Mp.OnReservationRequestResultReceived -= ReservationResult; Mp.OnReservationClearResultReceived -= CancelResult;
            TrackChecker.OnTrackOccupiedManually -= OccupancyDirty; TrackChecker.OnTrackClearedManually -= OccupancyDirty;
            foreach (var p in pending.Values) p.reply(new CommandResult { id = p.command.id, status = "outcomeUnknown", code = "CAPABILITY_UNAVAILABLE" });
            pending.Clear(); registry.Clear(); list.Clear(); dirty.Clear(); dirtyIds.Clear(); Reset();
        }
    }
}
