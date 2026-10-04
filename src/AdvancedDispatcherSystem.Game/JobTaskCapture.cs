using System;
using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;
using DV.Logic.Job;
using HarmonyLib;

namespace AdvancedDispatcherSystem.Game
{
    // Read-only projection of the native task tree. InProgress is the default
    // even for future children; SequentialTasks.currentTask controls eligibility.
    internal static class JobTaskCapture
    {
        internal static bool MovementCompleted(RoutePlan plan,IEnumerable<Job> jobs)
        {
            if(string.IsNullOrEmpty(plan.jobId)||plan.passengerRoute)return false;
            foreach(var job in jobs)if(job!=null && job.ID==plan.jobId) {
                var legs=new List<JobLeg>();
                for(int i=0;i<job.tasks.Count;i++)Read(job.tasks[i],i.ToString(),false,legs,(native,d)=>new JobLeg());
                return plan.taskIndex>=0 && plan.taskIndex<legs.Count && legs[plan.taskIndex].id==plan.taskId && legs[plan.taskIndex].progress=="completed";
            }
            return false;
        }
        internal static string ValidateMovement(RoutePlan plan,IEnumerable<Job> jobs,
            Func<Task,TaskData,JobLeg> capture = null, Func<JobLeg,string> validateConsist = null)
        {
            if(string.IsNullOrEmpty(plan.jobId))return null;
            Job job=null;foreach(var candidate in jobs)if(candidate!=null && candidate.ID==plan.jobId) {job=candidate;break;}
            if(job==null || job.State!=DV.ThingTypes.JobState.InProgress && job.State!=DV.ThingTypes.JobState.Available)return "JOB_TASK_UNAVAILABLE";
            var legs=new List<JobLeg>();
            bool available=job.State==DV.ThingTypes.JobState.Available;
            bool shunting = job.jobType.ToString().StartsWith("Shunting",StringComparison.OrdinalIgnoreCase);
            for(int i=0;i<job.tasks.Count;i++)Read(job.tasks[i],i.ToString(),!available,legs,shunting&&capture!=null?capture:((native,d)=>new JobLeg()));
            if(plan.taskIndex<0||plan.taskIndex>=legs.Count||legs[plan.taskIndex].id!=plan.taskId)return "JOB_TASK_CHANGED";
            if(available) {
                if(plan.taskIndex!=legs.FindIndex(l=>l.progress!="completed") || legs[plan.taskIndex].progress!="pending")return "JOB_TASK_CHANGED";
            }
            else if((shunting || !plan.passengerRoute) && legs[plan.taskIndex].progress!="active")return "JOB_TASK_CHANGED";
            if(shunting) {
                if(capture==null || validateConsist==null)return "JOB_TASK_UNAVAILABLE";
                var leg=legs[plan.taskIndex];
                var error=JobRouteRules.ShuntingMovementError(leg);
                if(error!=null)return error;
                if(leg.toTrack!=(plan.activeTo??plan.to))return "JOB_TASK_DESTINATION";
                foreach(var id in leg.cars)
                    if(Array.IndexOf(plan.trainCars??Array.Empty<string>(),id)<0)return "TRAIN_CHANGED";
                return validateConsist(leg);
            }
            // A passenger route can span later stops of the same order. A
            // shunting movement belongs to one currently executable operation.
            return null;
        }
        private static readonly System.Reflection.FieldInfo Current = AccessTools.Field(typeof(SequentialTasks), "currentTask");
        internal static void Read(Task task, string id, bool eligible, List<JobLeg> result,
            Func<Task,TaskData,JobLeg> capture, int depth = 0)
        {
            if(task==null || depth>16)throw new InvalidOperationException("JOB_TASK_TREE_UNAVAILABLE");
            var data=task.GetTaskData();
            if(data==null)throw new InvalidOperationException("JOB_TASK_DATA_UNAVAILABLE");
            if(data.nestedTasks!=null && data.nestedTasks.Count>0) {
                Task current=null;
                if(task is SequentialTasks) {
                    if(Current==null)throw new NotSupportedException("JOB_SEQUENTIAL_API_UNSUPPORTED");
                    current=(Current.GetValue(task) as LinkedListNode<Task>)?.Value;
                }
                for(int i=0;i<data.nestedTasks.Count;i++) {
                    var child=data.nestedTasks[i];
                    Read(child,id+"/"+i,eligible && data.state!=TaskState.Done &&
                        (task is ParallelTasks || task is SequentialTasks && ReferenceEquals(child,current)),result,capture,depth+1);
                }
                return;
            }
            var leg=capture(task,data);
            leg.id=id;
            leg.progress=data.state==TaskState.Done?"completed":data.state!=TaskState.InProgress?"unknown":eligible?"active":"pending";
            result.Add(leg);
        }
    }
}
