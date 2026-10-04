using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    /// <summary>
    /// Pure route-order checks used by automatic native block release.
    /// The route arrays are already in movement order, so the first direction
    /// of a rail is not a signal that the list itself should be reversed.
    /// </summary>
    public static class RouteProgressRules
    {
        public static bool IsBehindCurrent(IEnumerable<string> orderedRouteTracks,
            IEnumerable<string> blockTracks, IEnumerable<string> occupiedTracks)
        {
            if (orderedRouteTracks == null || blockTracks == null || occupiedTracks == null)
                return false;
            var ordered = orderedRouteTracks.Where(id => !string.IsNullOrEmpty(id)).ToArray();
            var block = new HashSet<string>(blockTracks.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            var occupied = new HashSet<string>(occupiedTracks.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            if (ordered.Length == 0 || block.Count == 0 || occupied.Count == 0)
                return false;
            var blockIndices = ordered.Select((id, index) => new { id, index })
                .Where(x => block.Contains(x.id)).Select(x => x.index).ToArray();
            var occupiedIndices = ordered.Select((id, index) => new { id, index })
                .Where(x => occupied.Contains(x.id)).Select(x => x.index).ToArray();
            if (blockIndices.Length == 0 || occupiedIndices.Length == 0) return false;
            // `ordered` is the traversal order for both +1 and -1 routes.
            // The final occurrence of every protected track must precede the
            // earliest possible occupied occurrence. A repeated physical track
            // stays protected across its future traversal, but is releasable
            // once the complete consist has unambiguously left the loop.
            return blockIndices.Max() < occupiedIndices.Min();
        }
    }
}
