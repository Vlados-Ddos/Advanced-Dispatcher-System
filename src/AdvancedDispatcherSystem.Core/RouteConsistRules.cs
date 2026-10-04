using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    public static class RouteConsistRules
    {
        /// <summary>
        /// Persistent Jobs can remove every runtime TrainCar during Suspend
        /// while retaining the stable CarGUID and last authoritative sample.
        /// A route may stay watched during that bounded lifecycle gap only
        /// when every missing expected member is explicitly marked suspended
        /// and no replacement identity has appeared in the consist.
        /// </summary>
        public static bool IsTemporarilySuspended(string consist, IEnumerable<string> expectedIds,
            IEnumerable<CarState> liveCars, IDictionary<string, CarState> snapshots)
        {
            var expected = (expectedIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            if (expected.Length == 0 || snapshots == null) return false;
            var live = (liveCars ?? Array.Empty<CarState>()).ToArray();
            var liveIds = new HashSet<string>(
                live.Select(car => car.id).Where(id => !string.IsNullOrEmpty(id)),
                StringComparer.Ordinal);
            var missing = expected.Where(id => !liveIds.Contains(id)).ToArray();
            if (missing.Length == 0) return false;
            if (live.Any(car => expected.Contains(car.id, StringComparer.Ordinal) &&
                (car.consist != consist || car.derailed))) return false;
            // A new CarGUID in the same consist means a real consist edit or
            // replacement, not the suspended-object lifecycle we can safely
            // wait through.
            if (live.Any(car => car.consist == consist && !expected.Contains(car.id, StringComparer.Ordinal))) return false;
            return missing.All(id => snapshots.TryGetValue(id, out var state) &&
                state.consist == consist &&
                string.Equals(state.availability, "suspended", StringComparison.OrdinalIgnoreCase));
        }
    }
}
