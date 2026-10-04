using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private static bool RouteAssignmentBusy(RoutePlan route) => route.reservationState=="reserved"||route.reservationState=="releaseFailed"||route.lifecycle=="preparing"||
        new[]{"preview","recalculating","replacing","unconfirmed"}.Contains(route.recalculationState);
    // Manual route assignment is dispatch metadata, separate from native job
    // acceptance. It is persisted with the route record and therefore keeps
    // the same Route ID through reconnects and route edits.
    public string AssignRoute(string routeId, string playerId, bool clear = false)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(routeId ?? "", out var route) || Ended(route)) return "NOT_FOUND";
            if (RouteAssignmentBusy(route)) return "ROUTE_ASSIGNMENT_RESERVED";
            PlayerState player = default;
            if (!clear && (string.IsNullOrEmpty(playerId) || !players.TryGetValue(playerId, out player) || string.IsNullOrEmpty(player.identityKey)))
                return "PLAYER_UNAVAILABLE";
            var priorKey=route.assignedPlayerKey;var priorName=route.assignedPlayerName;var priorOwner=route.reservationOwner;
            route.assignedPlayerKey = clear ? null : player.identityKey;
            route.assignedPlayerName = clear ? null : player.name;
            route.reservationOwner = clear
                ? JobReservationOwner(route.jobId)
                : player.identityKey;
            if(!SaveRoutes()) {
                route.assignedPlayerKey=priorKey;route.assignedPlayerName=priorName;route.reservationOwner=priorOwner;
                return "ROUTE_ASSIGNMENT_SAVE_FAILED";
            }
            UpdateRoutes(true);
            return null;
        }
    }
    private string JobReservationOwner(string id) {
        var job=jobs.GetValueOrDefault(id??"");
        return job?.assignedPlayerKey??job?.ownerKey??(Capabilities.mode=="singleplayer"?"local":null);
    }
}
