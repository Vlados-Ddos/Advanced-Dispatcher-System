using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedDispatcherSystem.Core
{
    public static class JobRouteRules
    {
        public static bool Passenger(JobState job) => job?.type=="PassengerExpress" || job?.type=="PassengerLocal";
        public static bool Shunting(JobState job) => job != null &&
            (job.type?.StartsWith("Shunting", StringComparison.OrdinalIgnoreCase) == true ||
             job.legs?.Any(l => l != null && l.operation?.StartsWith("Shunting", StringComparison.OrdinalIgnoreCase) == true) == true);
        public static string Validate(JobState job,int index,string taskId,string from,string to,string[] via,bool existing=false)
        {
            if(job==null || job.dataQuality!="ready")return "JOB_TASK_UNAVAILABLE";
            // Native signal reservation is independent of accepting a job.
            // Available jobs may prepare the first pending movement while the
            // native task state remains pending and acceptance remains explicit.
            bool available=job.state=="Available";
            if(!job.active && !available)return "JOB_TASK_UNAVAILABLE";
            if(index<0 || index>=job.legs.Length || string.IsNullOrEmpty(taskId) || job.legs[index].id!=taskId)return "JOB_TASK_CHANGED";
            var task=job.legs[index];
            if(available) {
                if(index!=Array.FindIndex(job.legs,l=>l.progress!="completed") || task.progress!="pending")return "JOB_TASK_CHANGED";
            } else if((!existing || !Passenger(job)) && task.progress!="active")return "JOB_TASK_CHANGED";
            if(Shunting(job)) {
                var error = ShuntingMovementError(task);
                if(error != null) return error;
            }
            if(Passenger(job)) {
                var required=RequiredStops(job,from);
                if(required==null || required.Length==0)return "JOB_STOP_TRACK_UNAVAILABLE";
                var requested=(via??Array.Empty<string>()).Concat(new[]{to}).ToArray();int cursor=0;
                foreach(var track in requested)if(cursor<required.Length&&required[cursor]==track)cursor++;
                if(cursor!=required.Length || to!=required[required.Length-1])return "JOB_ROUTE_POINTS_REQUIRED";
            }
            else if(string.IsNullOrEmpty(task.toTrack) || to!=task.toTrack)return "JOB_TASK_DESTINATION";
            return null;
        }
        public static string ShuntingMovementError(JobLeg task)
        {
            if(task == null || task.type != "Transport" ||
                !string.IsNullOrEmpty(task.operation) && task.operation != "None")
                return "JOB_SHUNTING_PLANNER_UNSUPPORTED";
            if(string.IsNullOrEmpty(task.toTrack)) return "JOB_TASK_DESTINATION";
            if(task.cars == null || task.cars.Length == 0 || task.cars.Any(string.IsNullOrEmpty) ||
                task.cars.Distinct(StringComparer.Ordinal).Count() != task.cars.Length)
                return "JOB_TASK_UNAVAILABLE";
            return task.couplingRequired ? "JOB_WAGON_EXTRACTION_REQUIRED" : null;
        }
        public static string ShuntingConsistError(JobLeg task, IEnumerable<CarState> observed, string train)
        {
            var shape = ShuntingMovementError(task);
            if(shape != null) return shape;
            var cars = (observed ?? Array.Empty<CarState>()).Where(c => !string.IsNullOrEmpty(c.id)).ToDictionary(c => c.id, StringComparer.Ordinal);
            if(string.IsNullOrEmpty(train)) return "JOB_ROUTE_START_REQUIRED";
            var anchor = cars.Values.FirstOrDefault(c => c.id == train || c.consist == train);
            if(string.IsNullOrEmpty(anchor.consist)) return "JOB_ROUTE_START_REQUIRED";
            var required = new HashSet<string>(task.cars, StringComparer.Ordinal);
            foreach(var id in required) {
                if(!cars.TryGetValue(id, out var car)) return "JOB_TASK_UNAVAILABLE";
                if(car.consist != anchor.consist) return "JOB_WAGON_EXTRACTION_REQUIRED";
            }
            var members = cars.Values.Where(c => c.consist == anchor.consist).ToDictionary(c => c.id, StringComparer.Ordinal);
            if(members.Values.Any(c => !c.nativeTrainset || !c.couplersKnown || c.derailed ||
                !string.IsNullOrEmpty(c.availability) && c.availability != "available" ||
                string.IsNullOrEmpty(c.track1) || string.IsNullOrEmpty(c.track2))) return "JOB_TASK_UNAVAILABLE";
            if(members.Values.Any(c => !required.Contains(c.id) && !c.locomotive && c.vehicleCategory != "tender" && c.vehicleCategory != "slug"))
                return "JOB_WAGON_EXTRACTION_REQUIRED";
            if(!members.Values.Any(c => c.locomotive)) return "JOB_ROUTE_START_REQUIRED";
            // Native Trainset identity alone may be sampled during a split.
            // Require a complete reciprocal coupler chain, including any
            // attached engine/tender/slug, before claiming one continuous movement.
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>(); pending.Enqueue(task.cars[0]);
            while(pending.Count > 0) {
                var id = pending.Dequeue(); if(!visited.Add(id)) continue;
                var car = members[id];
                foreach(var neighbourId in new[] { car.coupledFront, car.coupledRear }) {
                    if(string.IsNullOrEmpty(neighbourId)) continue;
                    if(neighbourId == id) return "JOB_TASK_UNAVAILABLE";
                    if(!members.TryGetValue(neighbourId, out var neighbour)) return "JOB_TASK_UNAVAILABLE";
                    if(neighbour.coupledFront != id && neighbour.coupledRear != id) return "JOB_TASK_UNAVAILABLE";
                    pending.Enqueue(neighbourId);
                }
            }
            return visited.Count == members.Count ? null : "JOB_WAGON_EXTRACTION_REQUIRED";
        }
        public static string[] RequiredStops(JobState job,string from)
        {
            var result=new List<string>();
            foreach(var leg in job.legs.Where(l=>l.passengerStop && l.progress!="completed")) {
                if(string.IsNullOrEmpty(leg.toTrack))return null;
                if(result.Count==0 && leg.toTrack==from)continue;
                if(result.Count==0 || result[result.Count-1]!=leg.toTrack)result.Add(leg.toTrack);
            }
            return result.ToArray();
        }

        // `via` is the executable ordered path. Passenger/task points are
        // mandatory, while the dispatcher may add approach or turnaround rails.
        // Keep that distinction in the route record without weakening ordered
        // stop validation.
        public static string[] ManualVia(JobState job,string from,string[] via)
        {
            var points=via??Array.Empty<string>();
            if(job==null || !Passenger(job))return points.ToArray();
            // Completed native stops stay native metadata on an existing route.
            // Completion must not silently relabel an old stop as a manual VIA.
            var required=new HashSet<string>(job.legs.Where(l=>l.passengerStop&&!string.IsNullOrEmpty(l.toTrack)).Select(l=>l.toTrack));
            return points.Where(id=>!required.Contains(id)).ToArray();
        }
    }
}
