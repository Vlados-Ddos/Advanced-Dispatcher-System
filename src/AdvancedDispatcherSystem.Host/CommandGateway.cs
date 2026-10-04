using AdvancedDispatcherSystem.Core;
using System.Security.Cryptography;

namespace AdvancedDispatcherSystem.Host;

public sealed class CommandGateway
{
    private sealed class Entry { public byte[] digest; public Task<CommandResult> task; public long until; }
    private readonly Dictionary<string, Entry> dedup = new();
    private readonly object sync = new();
    private readonly StateHub hub;
    private readonly Func<Command, Task<CommandResult>> executeGame;
    private readonly RateGate rate = new();
    private readonly SemaphoreSlim execution = new(1, 1);
    private int waiting;
    private readonly HashSet<string> allowed = new(StringComparer.Ordinal) { "acceptJob", "cancelJob", "assignJob", "unassignJob", "assignRoute", "unassignRoute", "setSwitch", "setTurntable", "loco", "setSignalMode", "setSignalAspect", "setShunting", "reserveSignal", "cancelSignalReservation", "planRoute", "editRoutePoints", "cancelRoute", "applyRoute", "recalculateRoute", "cancelRouteEdit", "confirmRouteEdit", "advanceRouteStage", "reserveRouteSignals", "releaseRouteSignals", "reserveRoute", "releaseRoute", "completeRoute", "rescan" };
    public CommandGateway(StateHub hub, GameConnection game) : this(hub, game.Execute) { }
    public CommandGateway(StateHub hub, Func<Command,Task<CommandResult>> executeGame) { this.hub=hub;this.executeGame=executeGame; }
    public Task<CommandResult> Run(Command c, AuthSession user)
    {
        CommandResult Reject(string code) => new() { id = c?.id ?? "", status = "rejected", code = code };
        if (c == null || string.IsNullOrWhiteSpace(c.id) || c.id.Length > 80 || c.kind == null || !allowed.Contains(c.kind) || !TrackGraph.Finite(c.value)) return Task.FromResult(Reject("INVALID_COMMAND"));
        if (user == null || (user.role != "dispatcher" && user.role != "admin") || (c.kind == "rescan" || c.kind == "loco") && user.role != "admin") return Task.FromResult(Reject("FORBIDDEN"));
        if (!rate.Allow((c.kind=="cancelRoute"?"cancel:":"command:")+user.name, c.kind=="cancelRoute"?10:30, 1000)) return Task.FromResult(Reject("RATE_LIMITED"));
        c.actor = user.name; c.role = user.role; c.deadline = 0; c.route=null;c.routeId=null;
        if(new[]{c.target,c.action,c.jobId,c.taskId,c.from,c.to,c.train,c.routeEditId}.Any(id=>id!=null&&(id.Length>256||id.Any(char.IsControl))))return Task.FromResult(Reject("INVALID_COMMAND"));
        if((c.kind=="planRoute"||c.kind=="editRoutePoints")&&(c.via==null||c.via.Length>128||c.via.Any(id=>string.IsNullOrEmpty(id)||id.Length>256||id.Any(char.IsControl))))return Task.FromResult(Reject("INVALID_COMMAND"));
        if(c.reservationMode!=null && c.reservationMode!="none" && c.reservationMode!="normal" && c.reservationMode!="protected")return Task.FromResult(Reject("INVALID_RESERVATION"));
        string key = user.name + ":" + c.id;
        byte[] digest = SHA256.HashData(Json.Bytes(c));
        lock (sync)
        {
            long now = Environment.TickCount64;
            if (dedup.TryGetValue(key, out var old)) return CryptographicOperations.FixedTimeEquals(digest, old.digest) ? old.task : Task.FromResult(Reject("COMMAND_ID_REUSED"));
            if (dedup.Count >= 4096)
            {
                foreach (var item in dedup.Where(x => x.Value.until < now && x.Value.task.IsCompleted).ToArray()) dedup.Remove(item.Key);
                if (dedup.Count >= 4096) return Task.FromResult(Reject("QUEUE_FULL"));
            }
            var validation = hub.Validate(c);
            if (validation != null) return Task.FromResult(Reject(validation));
            if (waiting >= (c.kind=="cancelRoute"?80:64)) return Task.FromResult(Reject("QUEUE_FULL"));
            Interlocked.Increment(ref waiting);
            var task = c.kind=="cancelRoute" ? CancelImmediately(c,user) : ExecuteSerial(c, user);
            dedup.Add(key, new Entry { digest = digest, task = task, until = now + 600000 }); return task;
        }
    }
    private async Task<CommandResult> CancelImmediately(Command c,AuthSession user)
    {
        try { return user.expires!=0&&user.expires<Environment.TickCount64?new CommandResult {id=c.id,status="rejected",code="FORBIDDEN"}:await Execute(c,user); }
        catch (RouteSearchLimitException) { return new CommandResult {id=c.id,target=c.target,status="rejected",code="ROUTE_SEARCH_LIMIT"}; }
        finally { Interlocked.Decrement(ref waiting); }
    }
    private async Task<CommandResult> ExecuteSerial(Command c, AuthSession user)
    {
        try {
            if (!await execution.WaitAsync(TimeSpan.FromSeconds(8))) return new CommandResult { id = c.id, status = "rejected", code = "QUEUE_FULL" };
            try {
                var error = user.expires != 0 && user.expires < Environment.TickCount64 ? "FORBIDDEN" : hub.Validate(c);
                try {return error == null ? await Execute(c, user) : new CommandResult { id = c.id, status = "rejected", code = error };}
                catch(RouteSearchLimitException) {return new CommandResult{id=c.id,target=c.target,status="rejected",code="ROUTE_SEARCH_LIMIT"};}
            }
            finally { execution.Release(); }
        }
        finally { Interlocked.Decrement(ref waiting); }
    }
    private async Task<CommandResult> Execute(Command c, AuthSession user)
    {
        CommandResult result;
        if(c.kind=="assignJob"||c.kind=="unassignJob") {var error=hub.AssignJob(c.target,c.action,c.kind=="unassignJob");result=new CommandResult{target=c.target,status=error==null?"applied":"rejected",code=error};}
        else if(c.kind=="assignRoute"||c.kind=="unassignRoute") {var error=hub.AssignRoute(c.target,c.action,c.kind=="unassignRoute");result=new CommandResult{target=c.target,status=error==null?"applied":"rejected",code=error,route=hub.Route(c.target)};}
        else if(c.kind=="planRoute")
        {
            var plan=await hub.PlanAsync(c.from,c.to,c.actor,c.train,preparing:true,via:c.via,jobId:c.jobId,taskIndex:c.taskIndex,taskId:c.taskId,avoidReservations:c.avoidReservations);
            result=plan==null?new CommandResult {status="rejected",code=hub.JobRouteError(c.jobId,c.taskIndex,c.taskId,c.from,c.to,c.via,train:c.train)??hub.RouteFailure(c.from,c.to,c.train,c.via)}:await PrepareRoute(plan.id,c.reservationMode??"none",c,user);
        }
        else if(c.kind=="recalculateRoute" || c.kind=="cancelRouteEdit" || c.kind=="editRoutePoints")
        {
            bool ok=c.kind=="cancelRouteEdit" ? hub.CancelRouteEdit(c.target,c.routeEditId) : hub.CreateRouteEditPreview(c.target,null,manual:true,requestedVia:c.kind=="editRoutePoints"?c.via:null);
            var route=hub.Route(c.target);
            result=new CommandResult {status=ok?"applied":"rejected",target=c.target,route=route,code=ok?null:route==null?"NOT_FOUND":route.reason??"ROUTE_RECALCULATION_STALE"};
        }
        else if(c.kind=="advanceRouteStage")
        {
            var next = hub.PrepareNextStage(c.target, c.reservationMode);
            result = next == null
                ? new CommandResult { status="rejected", target=c.target, code="STAGED_NEXT_LEG_UNAVAILABLE" }
                : await PrepareRoute(next.id, next.reservationMode, c, user);
        }
        else if(c.kind=="confirmRouteEdit")
        {
            var request = hub.RouteReplacementCommand(c.target, c.actor, c.routeEditId);
            if (request == null) result = new CommandResult { status="rejected", code="ROUTE_RECALCULATION_STALE", target=c.target };
            else
            {
                CommandResult applied;
                try { applied=await executeGame(request); }
                catch(Exception e) { Console.Error.WriteLine("ROUTE_EDIT_FAILED "+e.GetType().Name); applied=new CommandResult {status="outcomeUnknown",code="COMMAND_FAILED"}; }
                hub.Receipt(request, applied);
                var observed=hub.Route(c.target);
                bool confirmedByState=observed?.recalculationState=="none" && observed.editVersion==request.route.editVersion;
                if (applied.status == "applied" || confirmedByState)
                {
                    result = hub.CommitRouteEdit(c.target, applied.route ?? request.route)
                        ? new CommandResult { status="applied", target=c.target, route=hub.Route(c.target) }
                        : new CommandResult {status="outcomeUnknown",target=c.target,code="ROUTE_RECALCULATION_FAILED"};
                }
                else { hub.FailRouteEdit(c.target, applied.code ?? "ROUTE_RECALCULATION_FAILED",applied.status=="outcomeUnknown"); result = applied; }
            }
        }
        else if(c.kind=="applyRoute" || c.kind=="reserveRoute" || c.kind=="reserveRouteSignals")
            result=await PrepareRoute(c.target,c.kind=="applyRoute"?"none":c.reservationMode??"normal",c,user);
        else if(c.kind=="cancelRoute" || c.kind=="completeRoute" || c.kind=="releaseRoute" || c.kind=="releaseRouteSignals")
        {
            var route=hub.Route(c.target);
            if(route==null)result=new CommandResult {status="rejected",code="NOT_FOUND"};
            else if(route.recalculationState=="recalculating"||route.recalculationState=="replacing") result=new CommandResult {status="rejected",code="ROUTE_RECALCULATING",target=c.target};
            else
            {
                bool end=c.kind=="cancelRoute"||c.kind=="completeRoute";
                result=await executeGame(new Command {id=Guid.NewGuid().ToString("N"),kind=end?"endGameRoute":"releaseGameRoute",target=c.target,
                    action=end?(c.kind=="completeRoute"?"completed":"cancelled"):(route.endedAt>0?route.lifecycle:"active"),epoch=c.epoch,topologyRevision=c.topologyRevision,actor=c.actor});
                if(result.status=="applied"&&c.kind=="cancelRoute")hub.CancelRoute(c.target,c.actor);
            }
        }
        else
        {
            var internalCommand=Json.Read<Command>(Json.Bytes(c));internalCommand.id=Guid.NewGuid().ToString("N");
            result=await executeGame(internalCommand);
        }
        if(c.kind=="planRoute" && result.status=="applied")result.route=hub.Route(result.target);
        result.id=c.id;hub.Receipt(c,result);return result;
    }
    private async Task<CommandResult> PrepareRoute(string id,string mode,Command request,AuthSession user)
    {
        bool infrastructureApplied = false;
        async Task<CommandResult> Fail(string reason,string status="rejected")
        {
            hub.RouteOperation(id,"failed",reason);
            if (infrastructureApplied && status != "outcomeUnknown")
            {
                var cleanup = await executeGame(new Command { id=Guid.NewGuid().ToString("N"), kind="releaseGameRoute", target=id,
                    action="failed", epoch=request.epoch, topologyRevision=request.topologyRevision, actor=request.actor });
                hub.Receipt(request, cleanup);
                if (cleanup.status != "applied" && string.IsNullOrEmpty(reason)) reason = cleanup.code ?? "SWITCH_ROLLBACK_FAILED";
            }
            return new CommandResult {target=id,status=status,code=reason,warnings=hub.Route(id)?.warnings??[]};
        }
        try {
        if(mode=="protected"&&!hub.Capabilities.protectedReservations)return await Fail("PROTECTION_UNAVAILABLE");
        hub.RouteOperation(id,"preparing",mode:mode);
        string blocker=hub.RouteBlocker(id,mode!="none",mode=="none",request.forceReservation);if(blocker!=null)return await Fail(blocker);
        long deadline=Protocol.Now+180000;
        var attempted=new HashSet<string>();
        while(true)
        {
            var next=hub.RouteCommands(id,request.actor);
            if(next.Plan==null)return await Fail("NOT_FOUND");
            if(next.Commands.Length==0)break;
            var step=next.Commands[0];
            if(!attempted.Add(step.target)||attempted.Count>1024)return await Fail("ROUTE_PARTIAL");
            string error=Protocol.Now>deadline?"COMMAND_EXPIRED":user.expires!=0&&user.expires<Environment.TickCount64?"FORBIDDEN":hub.RouteBlocker(id,false,mode=="none",request.forceReservation)??hub.Validate(step);
            if(error!=null)return await Fail(error);
            var receipt=await executeGame(step);hub.Receipt(step,receipt);
            if(receipt.status!="applied")return await Fail(receipt.code??"ROUTE_PARTIAL",receipt.status=="outcomeUnknown"?"outcomeUnknown":"rejected");
            infrastructureApplied |= step.kind == "setSwitch";
        }
        // The game independently rereads geometry, occupancy, switch positions and
        // connected turntable mouths before acknowledging the complete operation.
        var registration=hub.RouteRegistration(id,request.actor,mode);
        if(registration==null)return await Fail("NOT_FOUND");
        string finalError=user.expires!=0&&user.expires<Environment.TickCount64?"FORBIDDEN":hub.RouteBlocker(id,false,mode=="none",request.forceReservation)??hub.Validate(registration);
        if(finalError!=null)return await Fail(finalError);
        var existing=hub.Route(id);
        if(mode=="none"&&existing?.reservationState=="reserved")
        {hub.RouteOperation(id,"active");return new CommandResult {target=id,status="applied",warnings=existing.warnings};}
        var confirmed=await executeGame(registration);hub.Receipt(registration,confirmed);
        if(confirmed.status!="applied")return await Fail(confirmed.code??"ROUTE_PARTIAL",confirmed.status);
        hub.RouteOperation(id,"active");
        return new CommandResult {target=id,status="applied",warnings=hub.Route(id)?.warnings??[]};
        }
        catch(Exception e) { Console.Error.WriteLine("ROUTE_EXECUTION_FAILED " + e.GetType().Name); return await Fail("COMMAND_FAILED", "outcomeUnknown"); }
    }
}
