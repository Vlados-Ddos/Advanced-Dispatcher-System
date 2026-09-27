using System;
using AdvancedDispatcherSystem.Core;
using global::Signals.Game;
using global::Signals.Common;
using global::Signals.Game.Railway;
using DVSignal = global::Signals.Game.Signal;
using Mp = global::Signals.Game.MultiplayerIntegration;
namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        public CommandResult Execute(Command c, Action<CommandResult> delayedResult)
        {
            CommandResult Result(string code = null) => new CommandResult { id = c.id, target = c.target, status = code == null ? "applied" : "rejected", code = code };
            if (!Ready || c.target == null || !c.target.StartsWith("s") || !int.TryParse(c.target.Substring(1), out int id) || !registry.TryGetValue(id, out var s)) return Result("NOT_FOUND");
            if (!cache.TryGetValue(id, out var old) || old.revision != c.expectedRevision || old.aspectIndex != s.CurrentAspectIndex || old.overrideIndex != s.ManualOverrideAspect || old.mode != s.Operation.ToString() || old.shunting != s.ShuntingAllowed || old.reserved != TrackReserver.HasReservation(s)) { Dirty(s); return Result("STALE_REVISION"); }
            if (pending.ContainsKey(id)) return Result("COMMAND_PENDING");
            CommandResult Unconfirmed() { Dirty(s); Sample(s); return Result("SIGNAL_STATE_NOT_CONFIRMED"); }
            string information = null;
            if (old.objectKind == "sign") return Result("SIGNAL_COMMAND_UNAVAILABLE");
            switch (c.kind)
            {
                case "setSignalMode":
                    if (!Enum.TryParse(c.action, out SignalOperationMode mode) || !Enum.IsDefined(typeof(SignalOperationMode), mode)) return Result("INVALID_VALUE");
                    s.ChangeOperationMode(mode);
                    if (s.Operation != mode) return Unconfirmed();
                    s.UpdateAspect(true);
                    if (s.Operation != mode) information = "SIGNAL_MODE_RETURNED_AUTO";
                    break;
                case "setSignalAspect":
                    if (c.index < -1 || c.index >= s.AllAspects.Length || c.index == -1 && s.Operation != SignalOperationMode.FullManual) return Result("INVALID_VALUE");
                    bool temporary = c.action == "TempOverride";
                    if (!string.IsNullOrEmpty(c.action) && !temporary) return Result("INVALID_VALUE");
                    if (temporary && c.index < 0) return Result("INVALID_VALUE");
                    if (s.Operation == SignalOperationMode.Automatic && !temporary) return Result("SIGNAL_AUTOMATIC_MODE");
                    s.SetAspectOverride(c.index);
                    if (s.ManualOverrideAspect != c.index) return Unconfirmed();
                    // One Unity-thread operation: no automatic evaluation can
                    // consume TempOverride before the requested aspect is set.
                    if (temporary) {
                        s.ChangeOperationMode(SignalOperationMode.TempOverride);
                        if (s.Operation != SignalOperationMode.TempOverride) return Unconfirmed();
                    }
                    s.UpdateAspect(true);
                    if (s.Operation == SignalOperationMode.FullManual && s.CurrentAspectIndex != c.index) return Unconfirmed();
                    if (s.Operation == SignalOperationMode.Automatic) information = "SIGNAL_MODE_RETURNED_AUTO";
                    else if (s.CurrentAspectIndex != c.index) information = "SIGNAL_OVERRIDE_RESTRICTED";
                    break;
                case "setShunting": if (c.value != 0 && c.value != 1) return Result("INVALID_VALUE"); s.SetShuntingStatus(c.value > 0);
                    if (s.ShuntingAllowed != (c.value > 0)) return Unconfirmed();
                    s.UpdateAspect(true); break;
                case "reserveSignal":
                case "cancelSignalReservation":
                    if (uncertainReservations.Contains(id)) return Result("SIGNAL_RESERVATION_UNCERTAIN");
                    if (c.value < 0 || c.value > 3600 || float.IsNaN(c.value) || float.IsInfinity(c.value)) return Result("INVALID_VALUE");
                    bool cancel = c.kind == "cancelSignalReservation";
                    if (protectedSignals.Contains(s)) return Result("SWITCH_LOCKED");
                    if (!cancel && (!s.AllowReserving || s.Parent != null)) return Result("RESERVATION_UNAVAILABLE");
                    if (!cancel && TrackReserver.HasReservation(s)) return Result("RESERVATION_CONFLICT");
                    if (!cancel) { var unsafeReason=ReservationSwitchError(s); if(unsafeReason!=null)return Result(unsafeReason); }
                    if (Mp.IsMpRunning)
                    {
                        pending[id] = new Pending { command = c, reply = delayedResult, cancel = cancel };
                        try { if (cancel) Mp.SendReservationCancelRequest(s); else Mp.SendReservationRequest(s, c.value); }
                        catch (Exception e) { pending.Remove(id); throw new InvalidOperationException("MP_RESERVATION_REQUEST_FAILED", e); }
                        return null;
                    }
                    if (cancel) TrackReserver.ClearFromSignal(s);
                    else
                    {
                        bool ok = c.value > 0 ? TrackReserver.ReserveForSignal(s, c.value) : TrackReserver.ReserveForSignal(s);
                        if (!ok || !TrackReserver.HasReservation(s)) return Result("RESERVATION_CONFLICT"); s.AlignAllSwitches();
                    }
                    if (TrackReserver.HasReservation(s) != !cancel) return Unconfirmed();
                    s.UpdateAspect(true);
                    break;
                default: return Result("INVALID_COMMAND");
            }
            Dirty(s); Sample(s);
            var result = Result(); result.code = information;
            if (cache.TryGetValue(id, out var observed)) result.revision = observed.revision;
            return result;
        }
        private void ExpirePending()
        {
            var expired = new System.Collections.Generic.List<int>();
            foreach (var pair in pending) if(pair.Value.command.deadline < Protocol.Now) expired.Add(pair.Key);
            foreach (var id in expired) {
                var request=pending[id]; pending.Remove(id); uncertainReservations.Add(id);
                request.reply(new CommandResult{id=request.command.id,target=request.command.target,status="outcomeUnknown",code="COMMAND_TIMEOUT"});
            }
        }
        private void ReservationResult(int signal, bool success) => Complete(signal, success, false);
        private void CancelResult(int signal) => Complete(signal, true, true);
        private void Complete(int id, bool success, bool cancel)
        {
            if (uncertainReservations.Remove(id)) { if (registry.TryGetValue(id,out var late)) Dirty(late); return; }
            if (!pending.TryGetValue(id, out var p) || p.cancel != cancel) return;
            pending.Remove(id);
            bool observed = registry.TryGetValue(id, out var s) && TrackReserver.HasReservation(s) == !cancel;
            p.reply(new CommandResult { id = p.command.id, target = p.command.target, status = success && observed ? "applied" : "rejected", code = success && observed ? null : "RESERVATION_CONFLICT", warnings = new[] { "SIGNALS_SHARED_REQUEST" } });
            if (s != null) { s.UpdateAspect(true); Dirty(s); Sample(s); }
        }
        private static string ReservationSwitchError(DVSignal s)
        {
            // Native AlignAllSwitches may move trailing junctions. Check exactly
            // those junctions, not every track in an otherwise aligned block.
            s.Controller.UpdateBlocks();
            if (s.Block == null) return "RESERVATION_UNAVAILABLE";
            foreach (var travel in s.Block.Tracks) {
                if (!travel.IsJunctionTrack || travel.Direction != TrackDirection.In) continue;
                var j = travel.Track.inJunction;
                if (j == null || j.selectedBranch >= j.outBranches.Count) return "RESERVATION_UNAVAILABLE";
                if (j.outBranches[j.selectedBranch].track == travel.Track) continue;
                if (j.inBranch?.track != null && TrackChecker.IsOccupied(j.inBranch.track,CrossingCheckMode.IntersectionOnly)) return "SWITCH_OCCUPIED";
                foreach (var branch in j.outBranches)
                    if (branch?.track != null && TrackChecker.IsOccupied(branch.track,CrossingCheckMode.IntersectionOnly)) return "SWITCH_OCCUPIED";
            }
            return null;
        }
    }
}
