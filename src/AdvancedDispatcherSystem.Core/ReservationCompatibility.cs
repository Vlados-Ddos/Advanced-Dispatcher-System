using System;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    public static class ReservationCompatibility
    {
        public static bool CanPlanAlong(RoutePlan planned, RoutePlan reserved)
        {
            if(planned==null)return false;
            if(planned.reservationMode!="none")return CanShare(planned,reserved);
            var candidate=planned.Snapshot();candidate.reservationMode=reserved?.reservationMode;
            return CanShare(candidate,reserved);
        }
        public static bool CanShare(RoutePlan a, RoutePlan b)
        {
            if(a==null || b==null || string.IsNullOrEmpty(a.owner) || (a.reservationOwner??a.owner)!=(b.reservationOwner??b.owner) ||
                string.IsNullOrEmpty(a.train) || a.train!=b.train ||
                a.reservationMode!=b.reservationMode || (a.reservationMode!="normal" && a.reservationMode!="protected") ||
                a.tracks==null || b.tracks==null || a.directions?.Length!=a.tracks.Length || b.directions?.Length!=b.tracks.Length ||
                a.directions.Any(d=>d!=1&&d!=-1) || b.directions.Any(d=>d!=1&&d!=-1) ||
                a.trainCars==null || b.trainCars==null || a.trainCars.Length==0 || !a.trainCars.OrderBy(x=>x).SequenceEqual(b.trainCars.OrderBy(x=>x))) return false;
            for(int i=0;i<a.tracks.Length;i++) {
                int other=Array.IndexOf(b.tracks,a.tracks[i]);
                if(other>=0 && a.directions[i]!=b.directions[other])return false;
            }
            if((a.switches??Array.Empty<RouteStep>()).Any(s=>(b.switches??Array.Empty<RouteStep>()).Any(t=>s.id==t.id && s.branch!=t.branch)))return false;
            return !(a.turntables??Array.Empty<TurntableStep>()).Any(s=>(b.turntables??Array.Empty<TurntableStep>()).Any(t=>s.id==t.id &&
                (s.from!=t.from||s.to!=t.to||s.fromEnd!=t.fromEnd||s.toEnd!=t.toEnd)));
        }
    }
}
