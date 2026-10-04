import { passengerStops } from "./job-progress.js";

// An estimate, not a timetable or movement authority. Integrate each posted
// speed section using the route's requested turnout branches. Unknown-speed
// sections and stop dwell use explicit ranges exposed in the UI hint.
export function routeTime(route, store) {
  if(!route?.tracks?.length)return null;
  let lower=30,upper=90,limit=null,unknown=0;
  const branches=new Map((route.switches||[]).map(s=>[s.id,s.branch]));
  const byRail=new Map();
  for(const sign of store.signs.values())if(!sign.advance) {
    const key=sign.track+":"+sign.direction;
    if(!byRail.has(key))byRail.set(key,[]);
    byRail.get(key).push(sign);
  }
  for(const signs of byRail.values())signs.sort((a,b)=>a.direction*(a.span-b.span));
  const add=(distance)=>{
    if(distance<=0)return;
    lower+=distance/(limit?Math.max(5,limit*.8)/3.6:40/3.6);
    upper+=distance/(limit?Math.max(3,limit*.5)/3.6:15/3.6);
    if(!limit)unknown+=distance;
  };
  for(const [index,id] of route.tracks.entries()) {
    const track=store.tracks.get(id),direction=route.directions?.[index];
    if(!track || ![1,-1].includes(direction))return null;
    const start=index===0?route.startSpan??(direction>0?0:track.length):direction>0?0:track.length;
    let span=start;
    const signs=byRail.get(id+":"+direction)||[];
    for(const sign of signs) {
      const values=(sign.speeds||[]).filter((speed,n)=>speed>0 && ((sign.branches?.[n]??-1)<0 || branches.get(sign.junction)===(sign.branches?.[n])));
      if(!values.length)continue;
      if(direction*(sign.span-start)>=0) {add(Math.abs(sign.span-span));span=sign.span;}
      limit=Math.min(...values);
    }
    add(Math.max(0,direction>0?track.length-span:span));
  }
  const job=store.jobs.get(route.jobId),stops=job?passengerStops(job).filter(s=>s.progress!=="completed").length:0;
  lower+=stops*60;upper+=stops*180;
  return {min:Math.max(1,Math.floor(lower/60)),max:Math.max(1,Math.ceil(upper/60)),stops,unknown};
}
