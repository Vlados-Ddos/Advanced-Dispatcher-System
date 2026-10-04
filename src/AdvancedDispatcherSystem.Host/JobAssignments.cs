using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    // Dispatch responsibility is separate from native acceptance and task
    // completion. DV Multiplayer's OwnedBy is an unused, unsaved auto-property;
    // writing it would neither replicate nor persist an assignment.
    private sealed class Assignment { public string job, player, name; }
    private sealed class AssignmentStore { public string epoch; public Assignment[] items=[]; }
    private readonly Dictionary<string,Assignment> jobAssignments=new();
    private string assignmentPath,assignmentEpoch;
    private void LoadAssignments(string directory)
    {
        assignmentPath=Path.Combine(directory,"job-assignments.json");
        if(!File.Exists(assignmentPath))return;
        try {
            if(new FileInfo(assignmentPath).Length>1024*1024)throw new InvalidDataException();
            var saved=Json.Read<AssignmentStore>(File.ReadAllBytes(assignmentPath));
            if(saved?.items==null||saved.items.Length>1000)throw new InvalidDataException();
            if(saved.items.Any(a=>a==null||string.IsNullOrEmpty(a.job)||a.job.Length>160||string.IsNullOrEmpty(a.player)||a.player.Length>160)||saved.items.Select(a=>a.job).Distinct().Count()!=saved.items.Length)throw new InvalidDataException();
            assignmentEpoch=saved.epoch;
            foreach(var item in saved.items)if(!string.IsNullOrEmpty(item?.job)&&!string.IsNullOrEmpty(item.player))jobAssignments[item.job]=item;
        } catch(Exception e) {
            Console.Error.WriteLine("JOB_ASSIGNMENTS_LOAD_FAILED "+e.GetType().Name);
            try {File.Copy(assignmentPath,assignmentPath+".invalid-"+Guid.NewGuid().ToString("N"));}
            catch(Exception backup) when(backup is IOException || backup is UnauthorizedAccessException) {Console.Error.WriteLine("JOB_ASSIGNMENTS_BACKUP_FAILED "+backup.GetType().Name);}
            jobAssignments.Clear();
        }
    }
    private void SaveAssignments()
    {
        if(assignmentPath==null)return;
        var bytes=Json.Bytes(new AssignmentStore{epoch=assignmentEpoch,items=jobAssignments.Values.ToArray()});
        if(bytes.Length>1024*1024)throw new InvalidDataException("JOB_ASSIGNMENTS_SIZE");
        File.WriteAllBytes(assignmentPath+".tmp",bytes);File.Move(assignmentPath+".tmp",assignmentPath,true);
    }
    private JobState WithAssignment(JobState job)
    {
        if(assignmentEpoch!=Topology?.epoch) {jobAssignments.Clear();assignmentEpoch=Topology?.epoch;}
        if(!jobAssignments.TryGetValue(job.id,out var assignment)) {
            if(job.assignedPlayerKey==null)return job;
            var cleared=Json.Read<JobState>(Json.Bytes(job));cleared.assignedPlayerId=cleared.assignedPlayerName=cleared.assignedPlayerKey=null;return cleared;
        }
        var copy=Json.Read<JobState>(Json.Bytes(job));
        var online=players.Values.FirstOrDefault(p=>p.identityKey==assignment.player);
        copy.assignedPlayerKey=assignment.player;copy.assignedPlayerName=online.name??assignment.name;copy.assignedPlayerId=online.id;
        return copy;
    }
    public string AssignJob(string jobId,string playerId,bool clear=false)
    {
        lock(sync) {
            if(!jobs.TryGetValue(jobId??"",out var job)||job.state!="Available"||job.dataQuality!="ready")return "JOB_STATE_CHANGED";
            if(routes.Values.Any(r=>r.jobId==jobId&&!Ended(r)&&RouteAssignmentBusy(r)))return "JOB_ASSIGNMENT_RESERVED";
            players.TryGetValue(playerId??"",out var player);
            if(!clear&&string.IsNullOrEmpty(player.identityKey))return "PLAYER_UNAVAILABLE";
            if(assignmentEpoch!=Topology?.epoch){jobAssignments.Clear();assignmentEpoch=Topology?.epoch;}
            jobAssignments.TryGetValue(jobId,out var previous);
            if(!clear&&previous==null&&jobAssignments.Count>=1000)return "QUEUE_FULL";
            if(clear)jobAssignments.Remove(jobId);
            else jobAssignments[jobId]=new Assignment{job=jobId,player=player.identityKey,name=player.name};
            try {SaveAssignments();}catch(Exception e) {
                if(previous==null)jobAssignments.Remove(jobId);else jobAssignments[jobId]=previous;
                Console.Error.WriteLine("JOB_ASSIGNMENT_SAVE_FAILED "+e.GetType().Name);return "JOB_ASSIGNMENT_SAVE_FAILED";
            }
            var updated=WithAssignment(job);jobs[jobId]=updated;
            bool routeChanged=false;
            foreach(var route in routes.Values.Where(r=>r.jobId==jobId&&!Ended(r))) {
                var owner=route.assignedPlayerKey??updated.assignedPlayerKey??updated.ownerKey??(Capabilities.mode=="singleplayer"?"local":null);
                if(route.reservationOwner!=owner){route.reservationOwner=owner;routeChanged=true;}
            }
            if(routeChanged)UpdateRoutes(true);
            Publish("delta",new GameBatch{epoch=Topology.epoch,topologyRevision=Topology.revision,jobs=[updated]});return null;
        }
    }
}
