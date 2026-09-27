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
    private readonly HashSet<string> allowed = new(StringComparer.Ordinal) { "setSwitch", "setTurntable", "loco", "setSignalMode", "setSignalAspect", "setShunting", "reserveSignal", "cancelSignalReservation", "planRoute", "cancelRoute", "applyRoute", "reserveRouteSignals", "releaseRouteSignals", "reserveRoute", "releaseRoute", "completeRoute", "rescan" };
    public CommandGateway(StateHub hub, GameConnection game) : this(hub, game.Execute) { }
    public CommandGateway(StateHub hub, Func<Command,Task<CommandResult>> executeGame) { this.hub=hub;this.executeGame=executeGame; }
    public Task<CommandResult> Run(Command c, AuthSession user)
    {
        CommandResult Reject(string code) => new() { id = c?.id ?? "", status = "rejected", code = code };
        if (c == null || string.IsNullOrWhiteSpace(c.id) || c.id.Length > 80 || c.kind == null || !allowed.Contains(c.kind) || !TrackGraph.Finite(c.value)) return Task.FromResult(Reject("INVALID_COMMAND"));
        if (user == null || (user.role != "dispatcher" && user.role != "admin") || (c.kind == "rescan" || c.kind == "loco") && user.role != "admin") return Task.FromResult(Reject("FORBIDDEN"));
        if (!rate.Allow((c.kind=="cancelRoute"?"cancel:":"command:")+user.name, c.kind=="cancelRoute"?10:30, 1000)) return Task.FromResult(Reject("RATE_LIMITED"));
        c.actor = user.name; c.role = user.role; c.deadline = 0; c.route=null;c.routeId=null;
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
        finally { Interlocked.Decrement(ref waiting); }
    }
    private async Task<CommandResult> ExecuteSerial(Command c, AuthSession user)
    {
        try {
            if (!await execution.WaitAsync(TimeSpan.FromSeconds(8))) return new CommandResult { id = c.id, status = "rejected", code = "QUEUE_FULL" };
            try {
                var error = user.expires != 0 && user.expires < Environment.TickCount64 ? "FORBIDDEN" : hub.Validate(c);
                return error == null ? await Execute(c, user) : new CommandResult { id = c.id, status = "rejected", code = error };
            }
            finally { execution.Release(); }
        }
        finally { Interlocked.Decrement(ref waiting); }
    }
    private async Task<CommandResult> Execute(Command c, AuthSession user)
    {
        CommandResult result;
        if(c.kind=="planRoute")
        {
            var plan=hub.Plan(c.from,c.to,c.actor,c.train,preparing:true,via:c.via);
            result=plan==null?new CommandResult {status="rejected",code=hub.RouteFailure(c.from,c.to,c.train,c.via)}:await PrepareRoute(plan.id,c.reservationMode??"none",c,user);
        }
        else if(c.kind=="applyRoute" || c.kind=="reserveRoute" || c.kind=="reserveRouteSignals")
            result=await PrepareRoute(c.target,c.kind=="applyRoute"?"none":c.reservationMode??"normal",c,user);
        else if(c.kind=="cancelRoute" || c.kind=="completeRoute" || c.kind=="releaseRoute" || c.kind=="releaseRouteSignals")
        {
            var route=hub.Route(c.target);
            if(route==null)result=new CommandResult {status="rejected",code="NOT_FOUND"};
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
        hub.RouteOperation(id,"preparing");
        string blocker=hub.RouteBlocker(id,mode!="none",mode=="none");if(blocker!=null)return await Fail(blocker);
        long deadline=Protocol.Now+180000;
        var attempted=new HashSet<string>();
        while(true)
        {
            var next=hub.RouteCommands(id,request.actor);
            if(next.Plan==null)return await Fail("NOT_FOUND");
            if(next.Commands.Length==0)break;
            var step=next.Commands[0];
            if(!attempted.Add(step.target)||attempted.Count>1024)return await Fail("ROUTE_PARTIAL");
            string error=Protocol.Now>deadline?"COMMAND_EXPIRED":user.expires!=0&&user.expires<Environment.TickCount64?"FORBIDDEN":hub.RouteBlocker(id,false,mode=="none")??hub.Validate(step);
            if(error!=null)return await Fail(error);
            var receipt=await executeGame(step);hub.Receipt(step,receipt);
            if(receipt.status!="applied")return await Fail(receipt.code??"ROUTE_PARTIAL",receipt.status=="outcomeUnknown"?"outcomeUnknown":"rejected");
            infrastructureApplied |= step.kind == "setSwitch";
        }
        // The game independently rereads geometry, occupancy, switch positions and
        // connected turntable mouths before acknowledging the complete operation.
        var registration=hub.RouteRegistration(id,request.actor,mode);
        if(registration==null)return await Fail("NOT_FOUND");
        string finalError=user.expires!=0&&user.expires<Environment.TickCount64?"FORBIDDEN":hub.RouteBlocker(id,false,mode=="none")??hub.Validate(registration);
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
