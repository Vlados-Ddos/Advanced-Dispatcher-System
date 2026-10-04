using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private static bool ValidRoutePath(RoutePlan route) => route != null &&
        !string.IsNullOrEmpty(route.id) && !string.IsNullOrEmpty(route.from) && !string.IsNullOrEmpty(route.to) &&
        ValidPath(route.tracks,route.directions,route.switches,route.turntables,route.itinerary,route.length,route.startSpan,route.remaining) &&
        ValidIds(route.via) && ValidIds(route.manualVia) && ValidIds(route.trainCars) && ValidIds(route.reservedSignals) && ValidIds(route.reservationTracks) &&
        ValidStages(route.stages);

    private static bool ValidStoredRoute(RoutePlan route) => ValidRoutePath(route) &&
        !string.IsNullOrEmpty(route.lifecycle) && !string.IsNullOrEmpty(route.reservationMode) && !string.IsNullOrEmpty(route.reservationState) &&
        route.warnings != null && ValidConflicts(route.conflicts) && ValidConflicts(route.history,false) &&
        (route.editPreview == null || !string.IsNullOrEmpty(route.editPreview.id) &&
            ValidPath(route.editPreview.tracks,route.editPreview.directions,route.editPreview.switches,route.editPreview.turntables,
                route.editPreview.itinerary,route.editPreview.length,route.editPreview.startSpan,route.editPreview.remaining) &&
            ValidStages(route.editPreview.stages) &&
            (route.editPreview.via==null || ValidIds(route.editPreview.via)) &&
            (route.editPreview.manualVia==null || ValidIds(route.editPreview.manualVia)) &&
            ValidIds(route.editPreview.reservationTracks) && ValidIds(route.editPreview.reservedSignals) && route.editPreview.warnings != null);

    private static bool ValidIds(string[] ids) => ids != null && ids.All(id=>!string.IsNullOrEmpty(id));
    private static bool ValidStages(RouteStage[] stages) => stages != null && stages.Length <= 128 && stages.All(s => s != null && !string.IsNullOrEmpty(s.id) &&
        !string.IsNullOrEmpty(s.from) && !string.IsNullOrEmpty(s.to) &&
        ValidPath(s.tracks,s.directions,s.switches,s.turntables,Array.Empty<RoutePoint>(),s.length,s.startSpan,s.remaining) &&
        ValidIds(s.reservedSignals) && ValidIds(s.reservationTracks));
    private static bool ValidConflicts(RouteConflict[] conflicts,bool unique=true) => conflicts != null &&
        conflicts.All(c=>c!=null&&!string.IsNullOrEmpty(c.code)) &&
        (!unique || conflicts.Select(c=>c.id??(c.code+":"+c.target+":"+c.track+":"+c.train)).Distinct().Count()==conflicts.Length);

    private static bool ValidPath(string[] tracks,int[] directions,RouteStep[] switches,TurntableStep[] tables,RoutePoint[] itinerary,
        double length,double start,double remaining) =>
        ValidIds(tracks) && tracks.Length>0 && directions!=null && tracks.Length==directions.Length && directions.All(d=>d==1||d==-1) &&
        switches!=null && switches.All(s=>s!=null&&!string.IsNullOrEmpty(s.id)&&s.branch>=0) &&
        tables!=null && tables.All(t=>t!=null&&!string.IsNullOrEmpty(t.id)&&!string.IsNullOrEmpty(t.from)&&!string.IsNullOrEmpty(t.to)) &&
        itinerary!=null && itinerary.All(p=>p!=null&&!string.IsNullOrEmpty(p.id)&&TrackGraph.Finite(p.distance)) &&
        TrackGraph.Finite(length) && length>=0 && TrackGraph.Finite(start) && TrackGraph.Finite(remaining);
}
