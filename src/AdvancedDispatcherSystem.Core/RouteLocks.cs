using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    // Unity-thread claims. Each route remains a consumer, including when the
    // same train and dispatcher reuse a compatible native reservation.
    public sealed class RouteLocks
    {
        private sealed class Claim { public string mode; public string[] tracks; public RouteStep[] switches; public RoutePlan context; }
        private readonly Dictionary<string, Claim> claims = new Dictionary<string, Claim>();
        public string Acquire(string route,string mode,string[] path,RouteStep[] steps,RoutePlan context=null)
        {
            if(claims.ContainsKey(route??""))return "ROUTE_ALREADY_RESERVED";
            return Replace(route,mode,path,steps,context);
        }
        public string Replace(string route,string mode,string[] path,RouteStep[] steps,RoutePlan context=null)
        {
            var error=CanReplace(route,mode,path,steps,context);if(error!=null)return error;
            var frozen=context?.Snapshot();if(frozen!=null)frozen.reservationMode=mode;
            claims[route]=new Claim {mode=mode,tracks=path.Distinct().ToArray(),
                switches=steps.Select(s=>new RouteStep{id=s.id,branch=s.branch}).ToArray(),context=frozen};
            return null;
        }
        public string CanReplace(string route,string mode,string[] path,RouteStep[] steps,RoutePlan context=null)
        {
            if(string.IsNullOrEmpty(route)||(mode!="normal"&&mode!="protected")||path==null||steps==null||
                path.Any(string.IsNullOrEmpty)||steps.Any(s=>s==null||string.IsNullOrEmpty(s.id))||
                steps.Select(s=>s.id).Distinct().Count()!=steps.Length)return "INVALID_RESERVATION";
            var wanted=new HashSet<string>(path);
            var next=context?.Snapshot();if(next!=null)next.reservationMode=mode;
            foreach(var entry in claims) {
                if(entry.Key==route)continue;
                var old=entry.Value;
                if(!old.tracks.Any(wanted.Contains)&&!old.switches.Any(s=>steps.Any(t=>t.id==s.id)))continue;
                if(!ReservationCompatibility.CanShare(next,old.context))return "ROUTE_RESERVED";
                if(old.switches.Any(s=>steps.Any(t=>t.id==s.id&&t.branch!=s.branch)))return "SWITCH_LOCKED";
            }
            return null;
        }
        public string TrackOwner(string id)=>id==null?null:claims.FirstOrDefault(c=>Array.IndexOf(c.Value.tracks,id)>=0).Key;
        public string SwitchOwner(string id)=>id==null?null:claims.FirstOrDefault(c=>c.Value.switches.Any(s=>s.id==id)).Key;
        public bool AllowsSwitch(string id,int branch)=>AllowsSwitch(id,branch,null);
        public bool AllowsSwitch(string id,int branch,string requester)
        {
            return claims.All(c=>c.Key==requester||c.Value.mode!="protected"||
                c.Value.switches.All(s=>s.id!=id||s.branch==branch));
        }
        /// <summary>
        /// Release only the completed physical tracks of one route. A route
        /// can keep later tracks and switch locks, and other route claims are
        /// left untouched. This mirrors native signal-block tail release and
        /// prevents a completed head from keeping a stale host-side lock.
        /// </summary>
        public void ReleaseTracks(string route,IEnumerable<string> releasedTracks)
        {
            if (route==null||releasedTracks==null||!claims.TryGetValue(route,out var claim))return;
            var released=new HashSet<string>(releasedTracks.Where(t=>!string.IsNullOrEmpty(t)),StringComparer.Ordinal);
            if(released.Count==0)return;
            claim.tracks=claim.tracks.Where(t=>!released.Contains(t)).ToArray();
        }
        public void Release(string route){if(route!=null)claims.Remove(route);}
        public void ReleaseSwitches(string route,IEnumerable<string> switches)
        {
            if(route==null||switches==null||!claims.TryGetValue(route,out var claim))return;
            var freed=new HashSet<string>(switches,StringComparer.Ordinal);
            claim.switches=claim.switches.Where(s=>!freed.Contains(s.id)).ToArray();
        }
        public void Clear()=>claims.Clear();
    }
}
