using System;
using System.Collections.Generic;

namespace AdvancedDispatcherSystem.Core
{
    // Owned by the Unity thread. A reservation claims the complete path atomically.
    public sealed class RouteLocks
    {
        private sealed class Claim { public string mode; public string[] tracks; public RouteStep[] switches; }
        private readonly Dictionary<string, Claim> claims = new Dictionary<string, Claim>();
        private readonly Dictionary<string, string> tracks = new Dictionary<string, string>();
        private readonly Dictionary<string, string> switches = new Dictionary<string, string>();
        public string Acquire(string route, string mode, string[] path, RouteStep[] steps)
        {
            if (string.IsNullOrEmpty(route) || (mode != "normal" && mode != "protected") || path == null || steps == null) return "INVALID_RESERVATION";
            if (claims.ContainsKey(route)) return "ROUTE_ALREADY_RESERVED";
            foreach (var id in path) if (id == null || tracks.ContainsKey(id)) return "ROUTE_RESERVED";
            foreach (var step in steps) if (step == null || step.id == null || switches.ContainsKey(step.id)) return "ROUTE_RESERVED";
            claims.Add(route, new Claim { mode = mode, tracks = (string[])path.Clone(), switches = (RouteStep[])steps.Clone() });
            foreach (var id in path) tracks[id] = route;
            foreach (var step in steps) switches[step.id] = route;
            return null;
        }
        public string TrackOwner(string id) => id != null && tracks.TryGetValue(id, out var owner) ? owner : null;
        public string SwitchOwner(string id) => id != null && switches.TryGetValue(id, out var owner) ? owner : null;
        public bool AllowsSwitch(string id, int branch)
        {
            if (id == null || !switches.TryGetValue(id, out var owner) || !claims.TryGetValue(owner, out var claim) || claim.mode != "protected") return true;
            return Array.Exists(claim.switches, s => s.id == id && s.branch == branch);
        }
        public void Release(string route)
        {
            if (route == null || !claims.TryGetValue(route, out var claim)) return;
            claims.Remove(route);
            foreach (var id in claim.tracks) if (TrackOwner(id) == route) tracks.Remove(id);
            foreach (var s in claim.switches) if (SwitchOwner(s.id) == route) switches.Remove(s.id);
        }
        public void Clear() { claims.Clear(); tracks.Clear(); switches.Clear(); }
    }
}
