using System;
using System.Linq;
using DV.Logic.Job;
using AdvancedDispatcherSystem.Core;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private bool TryJobCommand(Command command)
        {
            if(command.kind!="acceptJob" && command.kind!="cancelJob")return false;
            string error=ChangeJob(command);
            if(error==null) {
                var manager=JobsManager.Instance;
                var job=manager.currentJobs.Concat(manager.abandonedJobs).FirstOrDefault(j=>j.ID==command.target);
                if(job!=null)Main.Bridge?.Send(new WireFrame {kind="state",batch=new GameBatch {epoch=epoch,topologyRevision=topologyRevision,
                    jobs=new[]{ReadJob(job,manager,new System.Collections.Generic.Dictionary<string,Tuple<string,string>>())}}});
            }
            Result(new CommandResult {id=command.id,target=command.target,status=error==null?"applied":"rejected",code=error});
            return true;
        }
        private string ChangeJob(Command command)
        {
            var manager=JobsManager.Instance;
            if(manager==null)return "WORLD_NOT_READY";
            var registry=JobCars(manager);
            var job=manager.currentJobs.Concat(registry?.Keys.AsEnumerable()??Enumerable.Empty<Job>()).FirstOrDefault(j=>j?.ID==command.target);
            if(job==null)return "JOB_TASK_UNAVAILABLE";
            var player=CapturePlayers().FirstOrDefault(p=>p.host);
            using(new JobActionContext(player.id,player.name)) {
                if(command.kind=="cancelJob") {
                    if(job.State!=DV.ThingTypes.JobState.InProgress)return "JOB_STATE_CHANGED";
                    manager.AbandonJob(job);
                    RequestJobs();return job.State==DV.ThingTypes.JobState.Abandoned?null:"JOB_NATIVE_REJECTED";
                }
                if(job.State==DV.ThingTypes.JobState.InProgress)return null;
                if(job.State!=DV.ThingTypes.JobState.Available)return "JOB_STATE_CHANGED";
                var station=StationController.allStations.FirstOrDefault(s=>s.logicStation.availableJobs.Contains(job));
                var paper=Resources.FindObjectsOfTypeAll<JobOverview>().FirstOrDefault(o=>o!=null&&o.job==job);
                if(station==null||paper==null)return "JOB_OVERVIEW_UNAVAILABLE";
                // This is the authored office-anchor relationship also used by
                // DV Multiplayer's NetworkedStationController.RegisterJobValidator.
                var office=station.transform.parent?.name;
                var validator=Resources.FindObjectsOfTypeAll<JobValidator>().FirstOrDefault(v=>v!=null&&v.isActiveAndEnabled&&
                    string.Equals(v.transform.parent?.name+"_office_anchor",office,StringComparison.OrdinalIgnoreCase));
                if(validator==null)return "JOB_VALIDATOR_UNAVAILABLE";
                // Preserve all native checks (licenses, debt, concurrent jobs,
                // tutorial state and printer cooldown) and native booklet events.
                validator.ProcessJobOverview(paper);
                RequestJobs();
                return job.State==DV.ThingTypes.JobState.InProgress?null:"JOB_NATIVE_REJECTED";
            }
        }
    }
}
