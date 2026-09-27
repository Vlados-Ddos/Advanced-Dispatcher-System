using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private void ApplyTables(TurntableState[] values)
    {
        if (values == null || Graph == null) return;
        foreach (var value in values)
        {
            if (!Graph.Turntables.TryGetValue(value.id, out var def)) continue;
            turntables[value.id] = value;
            var bridge = Graph.Tracks[def.track];
            bridge.points = value.points;
            bridge.spans = [0, bridge.length];
            bridge.a = value.front == null ? [] : [new Link { track = value.front, end = value.frontEnd }];
            bridge.b = value.rear == null ? [] : [new Link { track = value.rear, end = value.rearEnd }];
            foreach (var end in def.ends)
            {
                var track = Graph.Tracks[end.track];
                var links = (end.end == 0 ? track.a : track.b).Where(x => x.track != def.track).ToList();
                if (end.track == value.front && end.end == value.frontEnd) links.Add(new Link { track = def.track, end = 0 });
                if (end.track == value.rear && end.end == value.rearEnd) links.Add(new Link { track = def.track, end = 1 });
                if (end.end == 0) track.a = links.ToArray(); else track.b = links.ToArray();
            }
        }
    }
}
