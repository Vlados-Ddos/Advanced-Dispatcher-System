using System;
namespace AdvancedDispatcherSystem.Core
{
    public static class RouteSignalRules
    {
        public static bool BlockFollows(RoutePlan route, int signalIndex, string[] tracks, int[] directions)
        {
            if (route?.tracks == null || route.directions == null || tracks == null || directions == null ||
                tracks.Length == 0 || tracks.Length != directions.Length) return false;
            if (signalIndex < 0 || signalIndex >= route.tracks.Length || route.tracks[signalIndex] != tracks[0]) return false;
            // Native TrackBlock starts at the controller's oriented starting
            // rail. Searching later in the route can accept a block from a
            // different occurrence of the same physical rail, especially on
            // loops or switch-to-switch sections. The start must be exact.
            for (int b = 0; b < tracks.Length && signalIndex + b < route.tracks.Length; b++)
                if (tracks[b] != route.tracks[signalIndex + b] || directions[b] != route.directions[signalIndex + b]) return false;
            return true;
        }
        public static bool Applies(SignalState signal, RoutePlan route, int index, double origin)
        {
            if (signal == null || signal.objectKind == "sign" || route?.tracks == null ||
                route.directions == null || route.tracks.Length != route.directions.Length ||
                index < 0 || index >= route.tracks.Length ||
                signal.track != route.tracks[index] || Math.Abs(signal.direction) != 1 ||
                signal.direction != route.directions[index] || !TrackGraph.Finite(signal.span) ||
                !TrackGraph.Finite(origin) || signal.direction * (signal.span - origin) < -0.01) return false;
            if (!signal.routeBindingRequired) return true;
            if (signal.routeIncoming == null || signal.routeBranches == null || signal.routeBranches.Length == 0)
                return false;
            // A head only applies if this exact traversal crosses its native
            // incoming-to-branch connection. Mere membership further down a loop
            // is insufficient, and ending before the fork chooses no head.
            for (int i = index; i + 1 < route.tracks.Length; i++) {
                if (route.tracks[i] != signal.routeIncoming) continue;
                if (signal.routeIncomingDirection!=0 && route.directions[i]!=signal.routeIncomingDirection) return false;
                int branch=Array.IndexOf(signal.routeBranches,route.tracks[i+1]);
                if(branch<0) return false;
                int direction=branch<(signal.routeBranchDirections?.Length??0)?signal.routeBranchDirections[branch]:1;
                return route.directions[i+1]==direction;
            }
            return false;
        }
    }
}
