using System;
using System.Collections.Generic;
using global::Signals.Game.Controllers;
using global::Signals.Game;
using DVSignal = global::Signals.Game.Signal;
namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        // SignalPlacer points the prefab towards approaching trains.
        // BasicSignalController.Orientation likewise uses -transform.forward.
        internal static int MovementDirection(TrackDirection facing) =>
            facing == TrackDirection.Out ? -1 : facing == TrackDirection.In ? 1 : 0;
        private void CaptureRouteBinding(DVSignal signal, out bool required, out string incoming, out string[] branches)
        {
            required = false; incoming = null; branches = Array.Empty<string>();
            var owner = signal;
            while (owner.Parent != null) owner = owner.Parent;
            if (!(owner.Controller is JunctionSignalController controller)) return;
            // The native GetActiveSignal/GetControllerShuntingSignal selects
            // selectedBranch % heads.Length. Preserve that relationship without
            // temporarily changing any junction or invoking UpdateBlocks.
            var heads = Array.IndexOf(controller.Signals, owner) >= 0 ? controller.Signals : controller.ShuntingSignals;
            if (heads == null || heads.Length <= 1) return;
            required = true;
            int head = Array.IndexOf(heads, owner);
            var junction = controller.Junction;
            if (head < 0 || junction == null) return;
            incoming = getTrack(junction.inBranch?.track);
            var result = new List<string>();
            for (int branch = 0; branch < junction.outBranches.Count; branch++)
                if (branch % heads.Length == head && getTrack(junction.outBranches[branch]?.track) is string id)
                    result.Add(id);
            branches = result.ToArray();
        }
    }
}
