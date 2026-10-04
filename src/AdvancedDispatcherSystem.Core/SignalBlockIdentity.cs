using System;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    // Reservation footprint identity, not TrackBlock's per-construction Id or
    // occupancy/aspect/owner state. Native ownership is validated separately.
    public static class SignalBlockIdentity
    {
        private static string Encode(string id) => id==null?"?":id.Length+":"+id;
        public static string Key(string[] tracks,int[] directions,string[] extraTracks)
        {
            tracks=tracks??Array.Empty<string>();directions=directions??Array.Empty<int>();
            var primary=tracks.Select((id,i)=>Encode(id)+":"+(i<directions.Length?directions[i]:0)).Distinct().OrderBy(s=>s,StringComparer.Ordinal);
            var extra=(extraTracks??Array.Empty<string>()).Except(tracks).Select(Encode).OrderBy(s=>s,StringComparer.Ordinal);
            return string.Join("|",primary)+"||"+string.Join("|",extra);
        }
        public static bool SameCapture(BlockState a,BlockState b) => a!=null&&b!=null&&
            a.id==b.id&&a.name==b.name&&a.source==b.source&&a.signal==b.signal&&a.occupied==b.occupied&&a.reserved==b.reserved&&
            a.length==b.length&&a.deadEnd==b.deadEnd&&a.quality==b.quality&&
            Key(a.tracks,a.directions,a.extraTracks)==Key(b.tracks,b.directions,b.extraTracks);
    }
}
