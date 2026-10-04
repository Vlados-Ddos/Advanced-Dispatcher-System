using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed partial class StateHub
{
    private readonly Dictionary<string, List<BlockState>> trackBlocks = new();
    private readonly Dictionary<string, List<SignalState>> trackSignals = new();
    private byte[] lastRoutes;
    private long nextRouteUpdate;
    private void IndexRouteObjects()
    {
        trackBlocks.Clear(); trackSignals.Clear();
        foreach (var b in blocks.Values) foreach (var id in b.tracks.Concat(b.extraTracks).Distinct())
        { if (!trackBlocks.TryGetValue(id, out var list)) trackBlocks[id] = list = new(); list.Add(b); }
        foreach (var s in signals.Values) if (s.objectKind != "sign" && s.track != null)
        { if (!trackSignals.TryGetValue(s.track, out var list)) trackSignals[s.track] = list = new(); list.Add(s); }
    }
    private CarState? RouteTrain(string train)
    {
        if (string.IsNullOrEmpty(train)) return null;
        if (cars.TryGetValue(train, out var car)) return car;
        CarState? first = null;
        foreach (var c in cars.Values) if (c.consist == train) { if (c.locomotive) return c; if (!first.HasValue) first = c; }
        return first;
    }
    private HashSet<string> RouteOwnCars(RoutePlan plan)
    {
        // Car GUIDs survive Trainset replacement. Car metadata and the native
        // route snapshot can arrive in either order after coupling, so an old
        // Trainset ID cannot make an already assigned car a foreign obstacle.
        var own = (plan.trainCars ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var anchor = RouteTrain(plan.trainCar ?? plan.train);
        if (!anchor.HasValue) return own;
        own.Add(anchor.Value.id);
        string consist = anchor.Value.consist;
        // Only extend ownership to newly coupled cars after all known route
        // cars agree on the live consist; a partial split is not a new claim.
        if (!string.IsNullOrEmpty(consist) && own.All(id => cars.TryGetValue(id, out var car) && car.consist == consist))
            foreach (var car in cars.Values) if (car.consist == consist) own.Add(car.id);
        return own;
    }
    private RoutePlan BuildPlan(string from, string to, string train, string[] via = null, string ignoreRouteId = null, CancellationToken cancellation = default, string owner=null, string jobId=null, bool avoidReservations = true)
    {
        var car = RouteTrain(train);
        if (!string.IsNullOrEmpty(train) && !car.HasValue) return null;
        var ownConsist = car?.consist;
        var contextJob=jobs.GetValueOrDefault(jobId??"");
        routes.TryGetValue(ignoreRouteId??"",out var previousRoute);
        var context=new RoutePlan {owner=owner,train=ownConsist,trainCars=car.HasValue?cars.Values.Where(c=>c.consist==ownConsist).Select(c=>c.id).ToArray():[],
            reservationOwner=previousRoute?.reservationOwner??contextJob?.assignedPlayerKey??contextJob?.ownerKey??(Capabilities.mode=="singleplayer"?"local":null),
            assignedPlayerKey=previousRoute?.assignedPlayerKey,assignedPlayerName=previousRoute?.assignedPlayerName,tracks=[],directions=[],switches=[],reservationMode="none"};
        bool compatible(RoutePlan other)=>ReservationCompatibility.CanPlanAlong(context,other);
        var reservedTracks = avoidReservations
            ? routes.Values.Where(other=>other.id!=ignoreRouteId&&!Ended(other)&&other.reservationState=="reserved"&&!compatible(other))
                .SelectMany(other=>other.reservationTracks).ToHashSet()
            : new HashSet<string>(StringComparer.Ordinal);
        var ownSignals=ignoreRouteId!=null&&routes.TryGetValue(ignoreRouteId,out var editing)?editing.reservedSignals:Array.Empty<string>();
        if (avoidReservations)
            foreach(var block in blocks.Values.Where(b=>b.reserved&&!ownSignals.Contains(b.signal)&&!routes.Values.Any(r=>r.reservationState=="reserved"&&r.reservedSignals.Contains(b.signal)&&compatible(r))))
                reservedTracks.UnionWith(block.tracks.Concat(block.extraTracks));
        double DynamicPenalty(string id)
        {
            double penalty = 0;
            if ((trackCars.TryGetValue(id, out var members) && members.Any(member => !cars.TryGetValue(member, out var occupant) || occupant.consist != ownConsist)) ||
                (nativeTrackCars.TryGetValue(id, out var nativeMembers) && nativeMembers.Any(member => !cars.TryGetValue(member, out var occupant) || occupant.consist != ownConsist)))
                penalty += 100000;
            if (occupancy.TryGetValue(id, out var sample) && sample.occupied && !membersOrOwn(id, ownConsist))
                penalty += 100000;
            if (reservedTracks.Contains(id))
                penalty += 150000;
            return ignoreRouteId!=null && penalty>0 ? double.PositiveInfinity : penalty;
        }
        bool membersOrOwn(string id, string consist) => trackCars.TryGetValue(id, out var members) && members.Any(member => cars.TryGetValue(member, out var occupant) && occupant.consist == consist);
        var routeFrom = car?.track1 ?? from;
        var requestedVia = via ?? new string[0];
        // Passenger orders are allowed to use a platform turnback/loop.  Use
        // the passenger-aware search so a repeated physical rail is split
        // into safe reservation stages while ordinary dispatcher and
        // shunting paths retain the strict unique-rail search contract.
        var passengerStops = JobRouteRules.Passenger(contextJob)
            ? JobRouteRules.RequiredStops(contextJob, routeFrom)
            : null;
        var plan = (JobRouteRules.Passenger(contextJob)
            ? Graph?.FindPassengerRouteVia(routeFrom, to, requestedVia, car?.direction ?? 0, car?.span1, DynamicPenalty, cancellation, passengerStops)
            : Graph?.FindRouteVia(routeFrom, to, requestedVia, car?.direction ?? 0, car?.span1, DynamicPenalty, cancellation));
        // A valid movement may revisit a physical rail after a turnback or
        // require a different switch phase.  Native reservation cannot hold
        // that as one footprint, so retain one logical route and expose
        // separately reservable stages instead of returning a misleading
        // complexity/no-route error.
        if (plan == null)
            plan = Graph?.FindStagedRouteVia(routeFrom, to, requestedVia, car?.direction ?? 0, car?.span1, DynamicPenalty, cancellation);
        if (plan != null) {plan.train = car?.consist;plan.trainCar=car?.id;plan.trainCars=context.trainCars;plan.owner=owner;plan.reservationOwner=context.reservationOwner;
            if (!plan.staged) { plan.activeFrom = plan.from; plan.activeTo = plan.to; plan.stageStatus = "single"; plan.stageIndex = 0; plan.stages = Array.Empty<RouteStage>(); }
            plan.assignedPlayerKey=context.assignedPlayerKey;plan.assignedPlayerName=context.assignedPlayerName;}
        return plan;
    }
    public RoutePlan Preview(string from, string to, string train = null, string[] via = null, CancellationToken cancellation = default, string jobId=null, int taskIndex=-1, string taskId=null, string editingId=null, string actor=null, bool avoidReservations=true)
    {
        lock (sync) { cancellation.ThrowIfCancellationRequested();
            routes.TryGetValue(editingId??"",out var existing);
            if(existing!=null && (Ended(existing)||existing.jobId!=jobId||existing.taskId!=taskId||existing.to!=to))return null;
            if(JobRouteError(jobId,taskIndex,taskId,RouteTrain(train)?.track1??from,to,via,existing!=null,train)!=null)return null;
            var remaining=RemainingRouteVia(existing,via??[],RouteTrain(train)?.track1);
            var plan = BuildPlan(from, to, train, remaining, string.IsNullOrEmpty(editingId)?null:editingId, cancellation:cancellation,owner:actor??existing?.owner,jobId:jobId,avoidReservations:avoidReservations);
            if (plan != null) {plan.jobId=jobId;plan.taskId=taskId;plan.taskIndex=taskIndex;
                plan.passengerRoute=JobRouteRules.Passenger(jobs.GetValueOrDefault(jobId??""));
                plan.via=via??[];
                plan.manualVia=JobRouteRules.ManualVia(jobs.GetValueOrDefault(jobId??""),from,plan.via);
                if(!string.IsNullOrEmpty(editingId)&&routes.TryGetValue(editingId,out var editing))plan.reservedSignals=editing.reservedSignals.ToArray();
                Evaluate(plan,editingId);} return plan; }
    }

    public async Task<RoutePlan> PreviewAsync(string from, string to, string train = null, string[] via = null,
        CancellationToken cancellation = default, string jobId=null, int taskIndex=-1, string taskId=null,
        string editingId=null, string actor=null, bool avoidReservations=true)
    {
        StateHub snapshot;
        lock (sync)
        {
            cancellation.ThrowIfCancellationRequested();
            snapshot = PlanningSnapshotLocked();
        }
        return await Task.Run(() => snapshot.Preview(from, to, train, via, cancellation,
            jobId, taskIndex, taskId, editingId, actor, avoidReservations), cancellation).ConfigureAwait(false);
    }
    private string[] RemainingRouteVia(RoutePlan route,string[] via,string currentTrack)
    {
        if(route?.jobId!=null && jobs.TryGetValue(route.jobId,out var job) && JobRouteRules.Passenger(job)) {
            var stops=job.legs.Where(l=>l.passengerStop).ToArray();
            via=via.Where(id=>!stops.Any(l=>l.toTrack==id)||stops.Any(l=>l.toTrack==id&&l.progress!="completed")).ToArray();
        }
        int at=route==null?-1:Array.IndexOf(route.tracks,currentTrack);
        return at<0?via:via.Where(id=>!route.via.Contains(id)||Array.IndexOf(route.tracks,id)>at).ToArray();
    }
    public string JobRouteError(string jobId,int taskIndex,string taskId,string from,string to,string[] via,bool existing=false,string train=null)
    {
        lock(sync) {
            if(string.IsNullOrEmpty(jobId)) return null;
            var job = jobs.GetValueOrDefault(jobId);
            var error = JobRouteRules.Validate(job,taskIndex,taskId,from,to,via,existing);
            return error ?? (JobRouteRules.Shunting(job)
                ? JobRouteRules.ShuntingConsistError(job.legs[taskIndex],cars.Values,train) : null);
        }
    }
    private static RouteEditPreview CopyPreview(RoutePlan plan, string reason, string affectedTrack, Topology topology) => new()
    {
        id = Guid.NewGuid().ToString("N"), reason = reason, affectedTrack = affectedTrack, topologyEpoch = topology?.epoch,
        staged = plan.staged, stageStatus = plan.stageStatus, stagedReason = plan.stagedReason,
        stagedReservationMode = plan.stagedReservationMode, stageIndex = plan.stageIndex,
        activeFrom = plan.activeFrom, activeTo = plan.activeTo,
        stages = plan.stages?.Select(s => new RouteStage { id=s.id, from=s.from, to=s.to, status=s.status,
            reservationMode=s.reservationMode, reservationState=s.reservationState, tracks=s.tracks?.ToArray()??Array.Empty<string>(),
            directions=s.directions?.ToArray()??Array.Empty<int>(), switches=s.switches?.Select(x=>new RouteStep{id=x.id,branch=x.branch}).ToArray()??Array.Empty<RouteStep>(),
            turntables=s.turntables?.Select(x=>new TurntableStep{id=x.id,from=x.from,to=x.to,position=x.position,fromEnd=x.fromEnd,toEnd=x.toEnd}).ToArray()??Array.Empty<TurntableStep>(),
            reservedSignals=s.reservedSignals?.ToArray()??Array.Empty<string>(), reservationTracks=s.reservationTracks?.ToArray()??Array.Empty<string>(), length=s.length,remaining=s.remaining,startSpan=s.startSpan }).ToArray()??Array.Empty<RouteStage>(),
        via = plan.via.ToArray(), manualVia = plan.manualVia?.ToArray() ?? Array.Empty<string>(),
        topologyRevision = topology?.revision ?? -1, createdAt = Protocol.Now,
        tracks = plan.tracks?.ToArray() ?? Array.Empty<string>(), directions = plan.directions?.ToArray() ?? Array.Empty<int>(),
        switches = plan.switches?.Select(s => new RouteStep { id = s.id, branch = s.branch }).ToArray() ?? Array.Empty<RouteStep>(),
        turntables = plan.turntables?.Select(s => new TurntableStep { id = s.id, from = s.from, to = s.to, position = s.position, fromEnd = s.fromEnd, toEnd = s.toEnd }).ToArray() ?? Array.Empty<TurntableStep>(),
        itinerary = plan.itinerary?.Select(p => new RoutePoint { kind = p.kind, id = p.id, track = p.track, distance = p.distance, current = p.current, boundary=p.boundary }).ToArray() ?? Array.Empty<RoutePoint>(),
        reservationTracks = plan.reservationTracks?.ToArray() ?? Array.Empty<string>(), reservedSignals = plan.reservedSignals?.ToArray() ?? Array.Empty<string>(),
        warnings = plan.warnings?.ToArray() ?? Array.Empty<string>(), length = plan.length, startSpan = plan.startSpan, remaining = plan.remaining
    };
    public bool CreateRouteEditPreview(string id, string reason, bool manual = false, string[] requestedVia=null)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var route) || Ended(route) || route.reservationState=="releaseFailed" || route.lifecycle=="preparing" || route.recalculationState=="replacing" || route.recalculationState=="unconfirmed") return false;
            if (route.recalculationState == "preview" && requestedVia==null) return true;
            var via=requestedVia??route.via;
            if(requestedVia!=null)reason="ROUTE_POINTS_CHANGED";
            reason ??= route.invalidationReason;
            var jobError=JobRouteError(route.jobId,route.taskIndex,route.taskId,RouteTrain(route.trainCar??route.train)?.track1??route.from,route.to,via,true,route.trainCar??route.train);
            if(jobError!=null) {route.reason=jobError;UpdateRoutes(true);return false;}
            if(manual || route.recalculationKey!=RouteNetworkKey())route.recalculationAttempts=0;
            route.recalculationKey=RouteNetworkKey();
            if (route.recalculationAttempts >= 3) { route.recalculationState = "failed"; route.reason = "ROUTE_RECALCULATION_LIMIT"; route.status = "warning"; UpdateRoutes(true); return false; }
            route.recalculationAttempts++;
            var start=RouteTrain(route.trainCar??route.train)?.track1;
            var remainingVia=RemainingRouteVia(route,via,start);
            RoutePlan candidate;
            try {candidate=BuildPlan(route.from,route.to,route.trainCar??route.train,remainingVia,route.id,owner:route.owner,jobId:route.jobId);}
            catch(RouteSearchLimitException) {
                route.recalculationState="failed";route.reason="ROUTE_SEARCH_LIMIT";route.editPreview=null;
                UpdateRoutes(true);return false;
            }
            if (candidate == null)
            {
                route.recalculationState = "failed"; route.reason = "NO_ALTERNATIVE_ROUTE"; route.status = "warning"; route.editPreview = null;
                UpdateRoutes(true); Log("routeRecalculationFailed", route.owner, route.id, route.reason, "route", "warning", "dispatcher"); return false;
            }
            candidate.reservedSignals=route.reservedSignals.ToArray();
            if(route.reservationState=="reserved" && route.invalidationReason=="PROTECTION_OCCUPIED" &&
                candidate.tracks.SequenceEqual(route.tracks)&&candidate.directions.SequenceEqual(route.directions))candidate.reservationTracks=route.reservationTracks.ToArray();
            candidate.via=via.ToArray();
            candidate.manualVia=JobRouteRules.ManualVia(string.IsNullOrEmpty(route.jobId)?null:jobs.GetValueOrDefault(route.jobId),route.from,via);
            Evaluate(candidate, route.id);
            var hard = new HashSet<string>(new[] { "TOPOLOGY_CHANGED", "TRACK_OCCUPIED", "PROTECTION_OCCUPIED", "OPPOSING_TRAIN", "BLOCK_OCCUPIED", "SIGNAL_RESERVED", "ROUTE_OVERLAP", "SWITCH_PLAN_CONFLICT", "OCCUPANCY_UNKNOWN", "TRAIN_UNAVAILABLE" });
            if (candidate.conflicts.Any(c => hard.Contains(c.code)))
            {
                route.recalculationState = "failed"; route.reason = "NO_ALTERNATIVE_ROUTE"; route.status = "warning"; route.editPreview = null;
                UpdateRoutes(true); Log("routeRecalculationFailed", route.owner, route.id, route.reason, "route", "warning", "dispatcher"); return false;
            }
            route.editPreview = CopyPreview(candidate, reason, route.conflicts.FirstOrDefault(c => !string.IsNullOrEmpty(c.track))?.track ?? candidate.conflicts.FirstOrDefault()?.track, Topology);
            route.recalculationState = "preview"; route.reason = reason; route.status = "warning";
            UpdateRoutes(true); Log("routeRecalculationPreview", route.owner, route.id, reason, "route", "warning", "dispatcher"); return true;
        }
    }
    public Command RouteReplacementCommand(string id, string actor, string previewId = null)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var route) || Ended(route) || route.recalculationState != "preview" || route.editPreview == null) return null;
            if(previewId!=null && previewId!=route.editPreview.id)return null;
            if (Topology == null || route.editPreview.topologyEpoch != Topology.epoch || route.editPreview.topologyRevision != Topology.revision)
            {
                var editedVia=route.editPreview.reason=="ROUTE_POINTS_CHANGED"?(route.editPreview.via??route.via):null;
                route.editPreview = null; route.recalculationState = "recalculating"; route.reason = "ROUTE_RECALCULATION_STALE"; UpdateRoutes(true);
                CreateRouteEditPreview(route.id, "ROUTE_RECALCULATION_STALE",requestedVia:editedVia); return null;
            }
            var p = Json.Read<RoutePlan>(Json.Bytes(route));
            var preview = route.editPreview;
            p.releasedTracks=[];p.releasedSignals=[];
            p.via=(preview.via??route.via).ToArray();
            p.manualVia=(preview.manualVia??route.manualVia??Array.Empty<string>()).ToArray();
            if(JobRouteError(p.jobId,p.taskIndex,p.taskId,RouteTrain(p.trainCar??p.train)?.track1??p.from,p.to,p.via,true,p.trainCar??p.train)!=null)return null;
            p.tracks = preview.tracks.ToArray(); p.directions = preview.directions.ToArray(); p.switches = preview.switches.Select(s => new RouteStep { id = s.id, branch = s.branch }).ToArray();
            p.turntables = preview.turntables.Select(s => new TurntableStep { id = s.id, from = s.from, to = s.to, position = s.position, fromEnd = s.fromEnd, toEnd = s.toEnd }).ToArray();
            p.itinerary = preview.itinerary.Select(s => new RoutePoint { kind = s.kind, id = s.id, track = s.track, distance = s.distance, current = s.current, boundary=s.boundary }).ToArray();
            p.reservedSignals=route.reservedSignals.ToArray(); p.reservationTracks=preview.reservationTracks.ToArray(); p.length = preview.length; p.startSpan = preview.startSpan; p.remaining = preview.remaining;
            p.staged=preview.staged; p.stageStatus=preview.stageStatus; p.stagedReason=preview.stagedReason; p.stagedReservationMode=preview.stagedReservationMode; p.stageIndex=preview.stageIndex; p.activeFrom=preview.activeFrom; p.activeTo=preview.activeTo;
            p.stages=preview.stages?.Select(s=>new RouteStage{id=s.id,from=s.from,to=s.to,status=s.status,reservationMode=s.reservationMode,reservationState=s.reservationState,tracks=s.tracks?.ToArray()??Array.Empty<string>(),directions=s.directions?.ToArray()??Array.Empty<int>(),switches=s.switches?.Select(x=>new RouteStep{id=x.id,branch=x.branch}).ToArray()??Array.Empty<RouteStep>(),turntables=s.turntables?.Select(x=>new TurntableStep{id=x.id,from=x.from,to=x.to,position=x.position,fromEnd=x.fromEnd,toEnd=x.toEnd}).ToArray()??Array.Empty<TurntableStep>(),reservedSignals=s.reservedSignals?.ToArray()??Array.Empty<string>(),reservationTracks=s.reservationTracks?.ToArray()??Array.Empty<string>(),length=s.length,remaining=s.remaining,startSpan=s.startSpan}).ToArray()??Array.Empty<RouteStage>();
            p.reason = null;p.nativeReason=null;
            Evaluate(p, route.id);
            var hard = new HashSet<string>(new[] { "TOPOLOGY_CHANGED", "TRACK_OCCUPIED", "PROTECTION_OCCUPIED", "OPPOSING_TRAIN", "BLOCK_OCCUPIED", "SIGNAL_RESERVED", "ROUTE_OVERLAP", "SWITCH_PLAN_CONFLICT", "OCCUPANCY_UNKNOWN", "TRAIN_UNAVAILABLE" });
            if (p.conflicts.Any(c => hard.Contains(c.code)))
            {
                var editedVia=preview.reason=="ROUTE_POINTS_CHANGED"?p.via:null;
                route.editPreview = null; route.recalculationState = "recalculating"; route.reason = "ROUTE_RECALCULATION_STALE"; UpdateRoutes(true);
                CreateRouteEditPreview(route.id, "ROUTE_RECALCULATION_STALE",requestedVia:editedVia); return null;
            }
            PopulateRouteReservations(p, route.reservationMode);
            p.editVersion=preview.id;
            p.recalculationState = "replacing"; p.reason = null; p.editPreview = null; p.lifecycle = "preparing"; p.status = "preparing"; p.reservationMode = route.reservationMode;
            route.recalculationState = "replacing"; route.status = "replacing"; UpdateRoutes(true);
            return new Command { id = Guid.NewGuid().ToString("N"), kind = "replaceGameRoute", target = p.id, route = p, reservationMode = p.reservationMode, epoch = Topology?.epoch, topologyRevision = Topology?.revision ?? -1, actor = actor };
        }
    }
    public bool CommitRouteEdit(string id, RoutePlan applied)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var route) || Ended(route) || applied == null || applied.id != route.id) return false;
            if(route.recalculationState=="none"&&route.editVersion==applied.editVersion)return true;
            if(route.recalculationState!="replacing"||route.editPreview?.id!=applied.editVersion)return false;
            route.tracks = applied.tracks; route.directions = applied.directions; route.switches = applied.switches; route.turntables = applied.turntables; route.itinerary = applied.itinerary;
            route.via=applied.via; route.manualVia=applied.manualVia??Array.Empty<string>();
            route.staged=applied.staged; route.stageStatus=applied.stageStatus; route.stagedReason=applied.stagedReason; route.stagedReservationMode=applied.stagedReservationMode; route.stageIndex=applied.stageIndex; route.activeFrom=applied.activeFrom; route.activeTo=applied.activeTo; route.stages=applied.stages??Array.Empty<RouteStage>();
            route.reservationTracks = applied.reservationTracks; route.reservedSignals = applied.reservedSignals; route.length = applied.length; route.startSpan = applied.startSpan; route.remaining = applied.remaining;
            route.releasedTracks=applied.releasedTracks??[];route.releasedSignals=applied.releasedSignals??[];
            route.editVersion=applied.editVersion;
            route.nativeReason=null;route.invalidationReason=null;route.canRecalculate=false;
            route.reservationState = applied.reservationMode=="none"?"none":"reserved"; route.reservationMode = applied.reservationMode; route.lifecycle = route.status = "active"; route.reason = null; route.warnings = Array.Empty<string>(); route.editPreview = null; route.recalculationState = "none"; route.recalculationAttempts = 0;
            UpdateRoutes(true); Log("routeEdited", route.owner, route.id, "recalculation", "route", "info", "dispatcher"); return true;
        }
    }
    internal void FailRouteEdit(string id, string reason, bool outcomeUnknown=false)
    {
        lock (sync) if (routes.TryGetValue(id ?? "", out var route) && !Ended(route)) {
            if(!outcomeUnknown)route.editPreview=null;
            route.recalculationState = outcomeUnknown?"unconfirmed":"failed";
            route.status = "warning"; route.reason = reason; UpdateRoutes(true);
        }
    }
    public bool CancelRouteEdit(string id, string previewId=null)
    {
        lock(sync) {
            if(!routes.TryGetValue(id??"",out var route)||Ended(route)||route.recalculationState!="preview"||route.editPreview==null || previewId!=null&&route.editPreview.id!=previewId)return false;
            route.editPreview=null;route.recalculationState="cancelled";route.recalculationKey=RouteNetworkKey();
            UpdateRoutes(true);return true;
        }
    }
    public string RouteFailure(string from,string to,string train=null, string[] via=null) {
        lock(sync) {
            var car=RouteTrain(train);
            if(!string.IsNullOrEmpty(train)&&!car.HasValue)return "TRAIN_UNAVAILABLE";
            if (Graph == null) return "WORLD_NOT_READY";
            if (via != null && via.Length > 0) {
                var start = car?.track1 ?? from;
                if (via.Length > 128 || via.Any(string.IsNullOrEmpty) || via.Any(id => !Graph.Tracks.ContainsKey(id)) || via.Distinct(StringComparer.Ordinal).Count() != via.Length || via.Contains(start) || via.Contains(to)) return "NO_ROUTE";
                if(Graph.FindRouteVia(start,to,via,car?.direction??0,car?.span1)!=null)return null;
                return Graph.HasOrderedWalk(start,to,via,car?.direction??0)?"ROUTE_MANEUVER_REQUIRED":"NO_ROUTE";
            }
            return Graph.RouteFailure(car?.track1 ?? from,to,car?.direction ?? 0);
        }
    }
    public RoutePlan Plan(string from, string to, string actor, string train = null, bool preparing = false, string[] via = null, string jobId = null, int taskIndex = -1, string taskId=null, bool avoidReservations=true)
    {
        lock (sync) return PlanImmediate(from,to,actor,train,preparing,via,jobId,taskIndex,taskId,avoidReservations);
    }
    private RoutePlan PlanImmediate(string from, string to, string actor, string train, bool preparing, string[] via, string jobId, int taskIndex, string taskId, bool avoidReservations)
    {
        if (routes.Values.Count(r=>!Ended(r)) >= 100) return null;
        if(JobRouteError(jobId,taskIndex,taskId,RouteTrain(train)?.track1??from,to,via,train:train)!=null)return null;
        var plan = BuildPlan(from, to, train, via,owner:actor,jobId:jobId,avoidReservations:avoidReservations); if (plan == null) return null;
        plan.lifecycle=preparing?"preparing":"active"; plan.id = Guid.NewGuid().ToString("N"); plan.owner = actor; plan.createdAt = Protocol.Now;
        plan.jobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId;
        plan.taskIndex = taskIndex >= 0 ? taskIndex : -1;
        plan.taskId = taskId;
        plan.manualVia = JobRouteRules.ManualVia(jobs.GetValueOrDefault(plan.jobId??""),plan.from,plan.via);
        plan.passengerRoute=plan.jobId!=null && JobRouteRules.Passenger(jobs.GetValueOrDefault(plan.jobId));
        plan.name = Graph.Tracks[plan.from].name + " → " + Graph.Tracks[plan.to].name;
        routes.Add(plan.id, plan); UpdateRoutes(true); Log("routePlanned", actor, plan.id, plan.name, "route", source: "dispatcher"); return plan;
    }
    public async Task<RoutePlan> PlanAsync(string from, string to, string actor, string train = null, bool preparing = false,
        string[] via = null, string jobId = null, int taskIndex = -1, string taskId=null, bool avoidReservations=true,
        CancellationToken cancellation = default)
    {
        StateHub snapshot;
        lock (sync)
        {
            if (routes.Values.Count(r=>!Ended(r)) >= 100) return null;
            if (JobRouteError(jobId,taskIndex,taskId,RouteTrain(train)?.track1??from,to,via,train:train)!=null) return null;
            cancellation.ThrowIfCancellationRequested();
            snapshot = PlanningSnapshotLocked();
        }
        var candidate = await Task.Run(() => snapshot.PlanImmediate(from,to,actor,train,preparing,via,jobId,taskIndex,taskId,avoidReservations), cancellation).ConfigureAwait(false);
        if (candidate == null) return null;
        lock (sync)
        {
            // The live world may have changed while search ran. Re-check the
            // task/topology boundary before committing the new route record;
            // PrepareRoute performs the final occupancy/native validation.
            if (routes.Values.Count(r=>!Ended(r)) >= 100 ||
                JobRouteError(jobId,taskIndex,taskId,RouteTrain(train)?.track1??from,to,via,train:train)!=null ||
                Topology == null || snapshot.Topology.epoch != Topology.epoch || snapshot.Topology.revision != Topology.revision)
                return null;
            candidate.id = Guid.NewGuid().ToString("N");
            candidate.lifecycle=preparing?"preparing":"active"; candidate.owner=actor; candidate.createdAt=Protocol.Now;
            candidate.jobId=string.IsNullOrWhiteSpace(jobId)?null:jobId; candidate.taskIndex=taskIndex>=0?taskIndex:-1; candidate.taskId=taskId;
            candidate.manualVia=JobRouteRules.ManualVia(jobs.GetValueOrDefault(candidate.jobId??""),candidate.from,candidate.via);
            candidate.passengerRoute=candidate.jobId!=null&&JobRouteRules.Passenger(jobs.GetValueOrDefault(candidate.jobId));
            candidate.name=Graph.Tracks[candidate.from].name+" → "+Graph.Tracks[candidate.to].name;
            routes.Add(candidate.id,candidate);UpdateRoutes(true);Log("routePlanned",actor,candidate.id,candidate.name,"route",source:"dispatcher");return candidate;
        }
    }
    public bool CancelRoute(string id, string actor)
    {
        lock (sync)
        {
            if (id == null || !routes.TryGetValue(id,out var plan)) return false;
            EndRoute(plan,"cancelled");UpdateRoutes(true); return true;
        }
    }
    public (RoutePlan Plan, Command[] Commands) RouteCommands(string id, string actor)
    {
        lock (sync)
        {
            if (!routes.TryGetValue(id ?? "", out var plan) || Ended(plan)) return default;
            Evaluate(plan);
            var commands = new List<Command>();
            foreach (var step in plan.turntables)
                if (!TurntableRules.Aligned(turntables.GetValueOrDefault(step.id), step) || Protocol.Now-(turntables.GetValueOrDefault(step.id)?.sampledAt ?? 0)>5000)
                    commands.Add(new Command { id = Guid.NewGuid().ToString("N"), routeId=plan.id, kind = "setTurntable", epoch = Topology.epoch, topologyRevision = Topology.revision, expectedRevision = turntables.GetValueOrDefault(step.id)?.revision ?? -1, target = step.id, from = step.from, to = step.to, fromEnd = step.fromEnd, toEnd = step.toEnd, index = step.position, actor = actor });
            foreach (var step in plan.switches)
                if (switches.TryGetValue(step.id, out var sw) && sw.branch != step.branch)
                    commands.Add(new Command { id = Guid.NewGuid().ToString("N"), routeId=plan.id, kind = "setSwitch", epoch = Topology.epoch, topologyRevision = Topology.revision, expectedRevision = sw.revision, target = step.id, branch = step.branch, actor = actor });
            return (Json.Read<RoutePlan>(Json.Bytes(plan)), commands.ToArray());
        }
    }
    private void UpdateRoutes(bool immediate = false)
    {
        if (!immediate && (routes.Count == 0 || Environment.TickCount64 < nextRouteUpdate)) return;
        nextRouteUpdate = Environment.TickCount64 + 500;
        foreach (var r in routes.Values) { Evaluate(r); if(Capabilities.status=="ready") {var before=r.reason;RefreshRecalculationState(r);if(before!=r.reason)Evaluate(r);} }
        foreach(var r in routes.Values.Where(Ended).OrderByDescending(r=>r.endedAt).Skip(200).ToArray())routes.Remove(r.id);
        var values = routes.Values.ToArray(); var bytes = Json.Bytes(values);
        if (lastRoutes != null && bytes.AsSpan().SequenceEqual(lastRoutes)) return;
        lastRoutes = bytes; SaveRoutes(); Publish("routes", values);
    }
    private void Evaluate(RoutePlan plan, string ignoreRouteId = null)
    {
        if(Ended(plan))return;
        var conflicts = new List<RouteConflict>(); var warnings = new HashSet<string>(); var dedup = new HashSet<string>();
        void Warn(string code, string kind, string target, string track = null, string train = null)
        {
            warnings.Add(code);
            if (conflicts.Count < 200 && dedup.Add(code + ":" + target + ":" + train)) conflicts.Add(new RouteConflict { code = code, kind = kind, target = target, track = track, train = train });
        }
        bool routeValid = plan.tracks != null && plan.directions != null && plan.tracks.Length == plan.directions.Length && plan.tracks.Length > 0;
        if (routeValid) for (int n = 0; n < plan.tracks.Length; n++) {
            if (!Graph.Tracks.ContainsKey(plan.tracks[n]) || Math.Abs(plan.directions[n]) != 1) { routeValid = false; break; }
            if (n + 1 < plan.tracks.Length && !Graph.RouteLinks(plan.tracks[n],plan.directions[n]>0?1:0)
                .Any(l=>l.track==plan.tracks[n+1] && (l.end==0?1:-1)==plan.directions[n+1])) {routeValid=false;break;}
        }
        if (!routeValid) {
            Warn("TOPOLOGY_CHANGED","routes",plan.id);
            plan.itinerary=[];plan.remaining=0;ResolveConflicts(plan,conflicts.ToArray());
            plan.warnings=warnings.ToArray();plan.status="warning";return;
        }
        var itinerary = new List<RoutePoint>(); var seenBlocks = new HashSet<string>();
        var ownCars = RouteOwnCars(plan);
        var activeDestination = plan.activeTo ?? plan.to;
        var car = RouteTrain(plan.trainCar ?? plan.train); int startIndex = 0; double startSpan = plan.startSpan, distance = 0;
        void TableWarnings(TurntableStep step) {
            var state=turntables.GetValueOrDefault(step.id);string bridge=Graph.Turntables[step.id].track;
            if (!TurntableRules.Aligned(state,step)) Warn("TURNTABLE_MISALIGNED","turntables",step.id,bridge);
            if(state==null || Protocol.Now-state.sampledAt>5000) Warn("TURNTABLE_UNKNOWN","turntables",step.id,bridge);
            else if(state.moving) Warn("TURNTABLE_BUSY","turntables",step.id,bridge);
            else if(!TurntableRules.Aligned(state,step)&&!state.available&&state.reason!=null) Warn(state.reason,"turntables",step.id,bridge);
            bool own=false,other=false;
            if(trackCars.TryGetValue(bridge,out var bridgeCars)) foreach(var member in bridgeCars) if(cars.TryGetValue(member,out var occupant)) {
                if(ownCars.Contains(member)) own=true;
                else { other=true;Warn("TRACK_OCCUPIED","tracks",bridge,bridge,occupant.consist); }
            }
            if(!occupancy.TryGetValue(bridge,out var bridgeOccupancy)||Protocol.Now-bridgeOccupancy.sampledAt>5000) Warn("OCCUPANCY_UNKNOWN","tracks",bridge);
            else if(bridgeOccupancy.occupied&&!own&&!other) Warn("TRACK_OCCUPIED","tracks",bridge);
        }
        if (car.HasValue)
        {
            startIndex = Array.IndexOf(plan.tracks, car.Value.track1);
            var crossing = startIndex<0 ? plan.turntables.FirstOrDefault(step=>Graph.Turntables[step.id].track==car.Value.track1) : null;
            if (crossing != null) {
                startIndex=Array.IndexOf(plan.tracks,crossing.to);
                startSpan=plan.directions[startIndex]>0 ? 0 : Graph.Tracks[crossing.to].length;
                var state=turntables.GetValueOrDefault(crossing.id);TableWarnings(crossing);
                int bridgeDirection=state?.front==crossing.to?-1:state?.rear==crossing.to?1:0;
                if(bridgeDirection!=0 && car.Value.direction!=0 && car.Value.direction!=bridgeDirection) Warn("TRAIN_OPPOSITE_ROUTE","trains",plan.train);
                var bridge=Graph.Tracks[car.Value.track1];
                distance=Math.Max(0,Math.Min(bridge.length,bridgeDirection<0?car.Value.span1:bridge.length-car.Value.span1));
                itinerary.Add(new RoutePoint {kind="turntables",id=crossing.id,track=bridge.id,current=true,distance=0});
            }
            else if (startIndex < 0) { Warn("TRAIN_OFF_ROUTE", "trains", plan.train); startIndex = 0; }
            else startSpan = car.Value.span1;
            if (crossing==null && car.Value.direction != 0 && car.Value.direction != plan.directions[startIndex]) Warn("TRAIN_OPPOSITE_ROUTE", "trains", plan.train);
        }
        else if (plan.train != null) Warn("TRAIN_UNAVAILABLE", "trains", plan.train);
        for (int i = startIndex; i < plan.tracks.Length; i++)
        {
            string id = plan.tracks[i]; int direction = plan.directions[i];
            if ((plan.releasedTracks ?? Array.Empty<string>()).Contains(id)) continue;
            if (!Graph.Tracks.TryGetValue(id, out var track)) { Warn("TOPOLOGY_CHANGED", "tracks", id); continue; }
            double origin = i == startIndex ? startSpan : direction > 0 ? 0 : track.length;
            bool ownTrainPresent = false, otherTrainPresent = false;
            var members = trackCars.TryGetValue(id, out var physical) ? physical.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            if (nativeTrackCars.TryGetValue(id, out var nativeMembers)) members.UnionWith(nativeMembers);
            foreach (string member in members)
            {
                if (!cars.TryGetValue(member, out var train))
                {
                    if (!ownCars.Contains(member)) Warn("TRACK_OCCUPIED", "tracks", id, id, member);
                    continue;
                }
                if (ownCars.Contains(member)) { ownTrainPresent = true; continue; }
                otherTrainPresent = true;
                Warn(train.direction != 0 && train.direction != direction ? "OPPOSING_TRAIN" : "TRACK_OCCUPIED", "tracks", id, id, train.consist);
            }
            if (!occupancy.TryGetValue(id, out var o) || Protocol.Now - o.sampledAt > 5000) Warn("OCCUPANCY_UNKNOWN", "tracks", id);
            else if (o.occupied && !otherTrainPresent && !ownTrainPresent) Warn("TRACK_OCCUPIED", "tracks", id);
            foreach (var other in routes.Values)
            {
                if (other == plan || other.id == ignoreRouteId || !Claims(other) || other.reservationState != "reserved" || ReservationCompatibility.CanPlanAlong(plan, other)) continue;
                // Native signal protection may include ExtraTracks outside the
                // movement path. Treat that authoritative reservation
                // footprint as an overlap too, otherwise avoidReservations=OFF
                // produces no warning and the later native rejection is
                // incorrectly reported as a route construction failure.
                var protectedTracks = (other.reservationTracks ?? Array.Empty<string>())
                    .Concat((other.tracks ?? Array.Empty<string>()).Except(other.releasedTracks ?? Array.Empty<string>()))
                    .ToHashSet(StringComparer.Ordinal);
                if (protectedTracks.Contains(id)) Warn("ROUTE_OVERLAP", "routes", other.id, id, other.train);
            }
            if (trackBlocks.TryGetValue(id, out var onTrack)) foreach (var block in onTrack)
            {
                if (block.reserved && !plan.reservedSignals.Contains(block.signal) && !routes.Values.Any(other=>other.reservationState=="reserved" && other.reservedSignals.Contains(block.signal) && ReservationCompatibility.CanPlanAlong(plan,other))) Warn("SIGNAL_RESERVED", "blocks", block.id, id);
                bool blockOwn = ownTrainPresent || block.trains.Any(ownCars.Contains) || block.tracks.Concat(block.extraTracks ?? Array.Empty<string>()).Any(track => nativeTrackCars.TryGetValue(track, out var native) && native.Any(ownCars.Contains));
                if (block.occupied && !block.trains.Any() && !blockOwn) Warn("BLOCK_OCCUPIED", "blocks", block.id, id);
                int at = Array.IndexOf(block.tracks, id);
                if (at < 0) continue; // Crossing/extra occupancy is a conflict, not a traversed block.
                if (at >= 0 && at < block.directions.Length && block.directions[at] != 0 && block.directions[at] != direction) continue;
                // Multiple signal views of the same physical track set share one itinerary entry.
                string key = string.Join("|", block.tracks.OrderBy(x => x, StringComparer.Ordinal));
                if (seenBlocks.Add(key)) itinerary.Add(new RoutePoint { kind = "blocks", id = block.id, track = id, distance = distance, current = i == startIndex });
            }
            if (trackSignals.TryGetValue(id, out var onSignals)) foreach (var s in onSignals)
            {
                if (!routeValid || !RouteSignalRules.Applies(s, plan, i, origin)) continue;
                bool boundary=i==plan.tracks.Length-1 && s.authorityTrack!=null && s.authorityTrack!=id && !plan.tracks.Contains(s.authorityTrack);
                itinerary.Add(new RoutePoint { kind = "signals", id = s.id, track = id, boundary=boundary, distance = distance + Math.Abs(s.span - origin) });
                if (!boundary && (s.stop || s.off)) Warn(s.off ? "SIGNAL_OFF" : "SIGNAL_STOP", "signals", s.id, id);
            }
            distance += Math.Max(0, direction > 0 ? track.length - origin : origin);
            if (i + 1 < plan.tracks.Length)
            {
                var link = Graph.RouteLinks(id, direction > 0 ? 1 : 0).FirstOrDefault(l => l.track == plan.tracks[i + 1] && (l.end == 0 ? 1 : -1) == plan.directions[i + 1]);
                if (link == null) Warn("TOPOLOGY_CHANGED", "tracks", id);
                else if (link.turntable != null)
                {
                    itinerary.Add(new RoutePoint { kind = "turntables", id = link.turntable, track = id, distance = distance });
                    var step = plan.turntables.First(s => s.id == link.turntable);
                    TableWarnings(step);
                    distance += link.crossingLength;
                }
                else if (link.junction != null)
                {
                    itinerary.Add(new RoutePoint { kind = "switches", id = link.junction, track = id, distance = distance });
                    if (!switches.TryGetValue(link.junction, out var sw) || sw.branch != link.branch) Warn("SWITCH_MISALIGNED", "switches", link.junction, id);
                    foreach (var other in routes.Values) if (other != plan && other.id != ignoreRouteId && Claims(other) && other.reservationState=="reserved" && other.switches.Any(s => s.id == link.junction && s.branch != link.branch)) Warn("SWITCH_PLAN_CONFLICT", "routes", other.id, id);
                }
            }
        }
        itinerary.Add(new RoutePoint { kind = "tracks", id = activeDestination, track = activeDestination, distance = distance });
        var bridges=plan.turntables.Where(s=>Graph.Turntables.ContainsKey(s.id)).Select(s=>Graph.Turntables[s.id].track);
        var remainingMovement = plan.tracks.Skip(startIndex).Except(plan.releasedTracks ?? Array.Empty<string>());
        foreach(var id in plan.reservationTracks.Except(remainingMovement.Concat(bridges))) {
            bool own=false,other=false;
            var protectionMembers = trackCars.TryGetValue(id,out var physicalMembers) ? physicalMembers.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            if(nativeTrackCars.TryGetValue(id,out var nativeProtection)) protectionMembers.UnionWith(nativeProtection);
            foreach(var member in protectionMembers) {
                if(!cars.TryGetValue(member,out var occupant)) { if(!ownCars.Contains(member)) { other=true;Warn("PROTECTION_OCCUPIED","tracks",id,id,member); } continue; }
                if(ownCars.Contains(member)){own=true;continue;}
                other=true;Warn("PROTECTION_OCCUPIED","tracks",id,id,occupant.consist);
            }
            if(!own&&!other&&occupancy.TryGetValue(id,out var state)&&state.occupied)Warn("PROTECTION_OCCUPIED","tracks",id,id);
        }
        plan.remaining = Math.Round(distance, 1); plan.itinerary = itinerary.OrderBy(p => p.distance).Take(2048).ToArray();
        // A native rejection is the summary of an existing concrete conflict, not a second warning.
        if (plan.nativeReason != null && plan.nativeReason != "SWITCH_MISALIGNED" && plan.nativeReason != "TURNTABLE_MISALIGNED" && !warnings.Contains(plan.nativeReason)) Warn(plan.nativeReason, "routes", plan.id);
        if (plan.reason != null && !IsRouteInvalidationReason(plan.reason) && plan.reason!="ROUTE_RECALCULATION_STALE" && plan.reason!="ROUTE_POINTS_CHANGED" && !warnings.Contains(plan.reason)) Warn(plan.reason, "routes", plan.id);
        ResolveConflicts(plan,conflicts.ToArray()); plan.warnings = warnings.ToArray(); plan.status = warnings.Count > 0 ? "warning" : plan.stageStatus=="available"?"stageAvailable":plan.recalculationState=="recalculating"?"recalculating":plan.lifecycle=="preparing"?"preparing":plan.reservationState=="reserved"?"reserved":"aligned";
    }
}
