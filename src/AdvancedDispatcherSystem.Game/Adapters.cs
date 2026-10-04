using System;
using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    public interface IPassengerAdapter : IDisposable
    {
        string Status { get; }
        void EnrichJob(DV.Logic.Job.Job job, JobState state);
        void EnrichTask(DV.Logic.Job.Task nativeTask, DV.Logic.Job.TaskData task, JobLeg leg);
        StationDef[] CaptureLocations(Func<RailTrack, string> trackId);
    }
    public interface IMultiplayerAdapter : IDisposable
    {
        string Version { get; }
        string Mode { get; }
        bool Authority { get; }
        bool ProtectedSwitches { get; }
        bool PublishSignalReservation(int signal, bool reserved);
        PlayerState[] CapturePlayers();
    }
    public interface ISignalsAdapter : IDisposable
    {
        bool Ready { get; }
        void Reset();
        void Tick(Func<RailTrack, string> trackId, Action<SignalState> signal, Action<BlockState> block, Action<string> removeSignal, Action<string> removeBlock, double budgetMs);
        CommandResult Execute(Command command, Action<CommandResult> delayedResult);
        string ReserveRoute(RoutePlan plan, Func<int, bool, bool> publish, out string[] acquired, Func<string> validateFootprint = null);
        string ReplaceRoute(RoutePlan plan, Func<int, bool, bool> publish, out string[] acquired, Func<bool> restoreInfrastructure = null, Func<string> validateFootprint = null);
        string ReleaseRoute(string route, Func<int, bool, bool> publish);
        // Release only native blocks whose complete physical footprint has
        // cleared the supplied consist tracks. The adapter owns native signal
        // identity and keeps shared route references intact.
        string ReleaseCompletedSegments(string route, string[] signalIds, string[] occupiedTracks,
            string[] orderedRouteTracks, bool reverse, Func<int, bool, bool> publish,
            out string[] releasedSignals, out string[] releasedTracks);
        void SeedRouteOccupancy(string route, IEnumerable<string> occupiedTracks);
        // Remaining native block coverage is separate from physically passed
        // route tracks. Null means unknown coverage and never authorizes release.
        string[] RouteProtectionTracks(string route);
        bool RouteReservationsIntact(string route);
        string RouteReservationIssue(string route);
    }
    public sealed class StandaloneAdapter : IMultiplayerAdapter
    {
        public string Version => "";
        public string Mode => "singleplayer";
        public bool Authority => true;
        public bool ProtectedSwitches => true;
        public bool PublishSignalReservation(int signal, bool reserved) => true;
        public PlayerState[] CapturePlayers()
        {
            var t = PlayerManager.PlayerTransform;
            if (t == null) return new PlayerState[0];
            var p = t.position - WorldMover.currentMove;
            return new[] { PlayerPose.Attach(new PlayerState { id = "local", identityKey = "local", name = "Player", host = true, x = p.x, z = p.z, yaw = t.eulerAngles.y, car = PlayerManager.Car == null ? null : PlayerManager.Car.CarGUID, sampledAt = Protocol.Now }, PlayerManager.Car) };
        }
        public void Dispose() { }
    }
}
