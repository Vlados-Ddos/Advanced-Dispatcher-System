using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using JobState = AdvancedDispatcherSystem.Core.JobState;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private readonly Dictionary<string, JobState> jobHistory = new Dictionary<string, JobState>();
        private readonly LinkedList<string> jobHistoryOrder = new LinkedList<string>();
        private readonly Dictionary<Job, double> terminalJobs = new Dictionary<Job, double>();
        private readonly Dictionary<string, string> jobStartDates = new Dictionary<string, string>();
        private IEnumerator jobCapture;
        private void StopJobCapture() { (jobCapture as IDisposable)?.Dispose(); jobCapture = null; jobsRunning = false; }
        private void StepJobs()
        {
            if (jobCapture == null && UnityEngine.Time.realtimeSinceStartup < nextJobs) return;
            UnityEngine.Profiling.Profiler.BeginSample("ADS.Jobs.Step");
            try {
                if (jobCapture == null) { nextJobs = UnityEngine.Time.realtimeSinceStartup + 2; jobCapture = CaptureJobs(); }
                if (!jobCapture.MoveNext()) StopJobCapture();
            }
            catch (Exception e) { StopJobCapture(); Main.Log("JOB_CAPTURE_FAILED", e); }
            finally { UnityEngine.Profiling.Profiler.EndSample(); }
        }
        private static bool SameLocations(StationDef[] a, StationDef[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) {
                var x = a[i]; var y = b[i];
                if (x.id != y.id || x.name != y.name || x.nameEn != y.nameEn || x.nameRu != y.nameRu || x.type != y.type || x.source != y.source || x.parent != y.parent ||
                    x.code != y.code || x.platform != y.platform || x.platformLabel != y.platformLabel || x.trackGroup != y.trackGroup || x.color != y.color ||
                    x.x != y.x || x.z != y.z || x.industry != y.industry || x.city != y.city || x.passenger != y.passenger ||
                    !x.tracks.SequenceEqual(y.tracks) || !x.spawnTracks.SequenceEqual(y.spawnTracks) || !x.searchNames.SequenceEqual(y.searchNames) ||
                    !SameStationTracks(x.stationTracks, y.stationTracks)) return false;
            }
            return true;
        }
        private static bool SameStationTracks(StationTrackDef[] a, StationTrackDef[] b)
        {
            a ??= new StationTrackDef[0]; b ??= new StationTrackDef[0];
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                var x = a[i]; var y = b[i];
                if (x == null || y == null || x.id != y.id || x.name != y.name || x.fullName != y.fullName || x.group != y.group || x.direction != y.direction) return false;
            }
            return true;
        }
        private void ResetJobHistory() { jobHistory.Clear(); jobHistoryOrder.Clear(); terminalJobs.Clear(); jobStartDates.Clear(); }
        private void JobChanged(Job job, bool announce = true)
        {
            if (!world || job == null) return;
            if (announce && job.State == DV.ThingTypes.JobState.InProgress)
            {
                var clock = DV.TimeKeeping.WorldClockController.Instance;
                if (clock != null)
                {
                    var time = clock.GetCurrentAnglesAndTimeOfDay();
                    if (time.validTime) jobStartDates[job.ID] = time.timeOfDay.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            RequestJobs();
            if (announce && multiplayer.Authority) {
                var actor = JobActionContext.Current;
                AdvancedDispatcherSystem.Core.PlayerState? local = actor == null && multiplayer.Mode == "singleplayer" ? multiplayer.CapturePlayers().FirstOrDefault() : null;
                ReportEvent("job" + job.State, job.ID, job.chainData?.chainOriginYardId + " → " + job.chainData?.chainDestinationYardId,
                    "job", actor: actor?.PlayerName ?? local?.name, actorId: actor?.PlayerId ?? local?.id);
            }
            if (job.State == DV.ThingTypes.JobState.Completed || job.State == DV.ThingTypes.JobState.Abandoned || job.State == DV.ThingTypes.JobState.Expired)
            {
                if (terminalJobs.Count < 250) terminalJobs[job] = Math.Max(0, job.State == DV.ThingTypes.JobState.Completed ? job.GetJobCompletionTime() : job.State == DV.ThingTypes.JobState.Expired ? 0 : job.GetTimeOnJob());
                foreach(var route in watchedRoutes.Values.Where(r=>r.state.lifecycle=="active"&&r.plan.jobId==job.ID).ToArray())
                    ReleaseWatchedRoute(route,job.State==DV.ThingTypes.JobState.Completed?"completed":"cancelled",null);
            }
        }
        private IEnumerator CaptureJobs()
        {
            if (jobsRunning) yield break;
            jobsRunning = true;
            var captureEpoch = epoch; var captureRevision = topologyRevision;
            var capturePassenger = passenger; var captureJobsRevision = jobsRevision;
            try
            {
                var manager = JobsManager.Instance;
                if (manager == null) { RequestJobs(); yield break; }
                var jobCars = JobCars(manager);
                if (jobCars == null) { RequestJobs(); yield break; }
                var locations = CaptureLocations();
                var source = new HashSet<Job>(jobCars.Keys);
                foreach (var job in manager.currentJobs) source.Add(job);
                // The game's own terminal lists can outlive the car registry.
                for (int i = Math.Max(0, manager.finishedJobs.Count - 125); i < manager.finishedJobs.Count; i++) source.Add(manager.finishedJobs[i]);
                for (int i = Math.Max(0, manager.abandonedJobs.Count - 125); i < manager.abandonedJobs.Count; i++) source.Add(manager.abandonedJobs[i]);
                foreach (var job in terminalJobs.Keys) source.Add(job);
                var result = new List<JobState>(); var links = new Dictionary<string, Tuple<string, string>>();
                foreach (var job in source.Where(j => j != null).OrderBy(j => j.ID, StringComparer.Ordinal))
                {
                    if (!world || building || epoch != captureEpoch || topologyRevision != captureRevision) yield break;
                    try
                    {
                        if (job == null) continue;
                        bool terminal = job.State != DV.ThingTypes.JobState.Available && job.State != DV.ThingTypes.JobState.InProgress;
                        JobState captured;
                        if (terminal && jobHistory.TryGetValue(job.ID, out var saved) && saved.state == job.State.ToString()) { terminalJobs.Remove(job); continue; }
                        captured = ReadJob(job, manager, links);
                        if (terminal)
                        {
                            if (!jobHistory.ContainsKey(captured.id)) jobHistoryOrder.AddLast(captured.id);
                            jobHistory[captured.id] = captured;
                            while (jobHistory.Count > 250) { jobStartDates.Remove(jobHistoryOrder.First.Value); jobHistory.Remove(jobHistoryOrder.First.Value); jobHistoryOrder.RemoveFirst(); }
                            terminalJobs.Remove(job);
                        }
                        else
                        {
                            var previous = lastJobs.FirstOrDefault(j => j.id == captured.id);
                            if (captured.dataQuality == "stale" && !JobSnapshotQuality.KeepMissingRuntime(previous, captured.cars, false))
                            {
                                // replaceJobs is authoritative. A previously
                                // bound car that is now absent and not marked
                                // suspended means the order left the game;
                                // retaining stale here creates a ghost order.
                                jobHistory.Remove(captured.id); jobHistoryOrder.Remove(captured.id); continue;
                            }
                            jobHistory.Remove(captured.id); jobHistoryOrder.Remove(captured.id); result.Add(captured);
                        }
                    }
                    catch (Exception e)
                    {
                        Main.Log("JOB_CAPTURE_FAILED", e);
                        // Keep an explicitly stale record, rather than making the job vanish
                        // for one sweep. No stale job data is usable for task routing/colors.
                        var previous = lastJobs.FirstOrDefault(j => j.id == job.ID);
                        var stale = previous == null ? new JobState { id = job.ID, type = job.jobType.ToString() }
                            : Newtonsoft.Json.JsonConvert.DeserializeObject<JobState>(Newtonsoft.Json.JsonConvert.SerializeObject(previous));
                        stale.state = job.State.ToString(); stale.active = job.State == DV.ThingTypes.JobState.InProgress;
                        stale.dataQuality = "stale"; stale.elapsedKnown = false;
                        foreach (var id in links.Where(p => p.Value.Item1 == job.ID).Select(p => p.Key).ToArray()) links.Remove(id);
                        jobHistory.Remove(job.ID); jobHistoryOrder.Remove(job.ID); result.Add(stale);
                    }
                    yield return null;
                }
                if (!world || building || epoch != captureEpoch || topologyRevision != captureRevision || capturePassenger != passenger || captureJobsRevision != jobsRevision) { RequestJobs(); yield break; }
                // `replaceJobs` is the authoritative current Orders snapshot.
                // Terminal jobs stay in the bounded in-memory history for
                // event timing, but publishing that history here resurrects
                // completed/expired orders in the live Orders tab.
                carJobs.Clear(); foreach (var pair in links) carJobs[pair.Key] = pair.Value;
                lastJobs = result.ToArray();
                bool locationsChanged = !SameLocations(lastLocations, locations);
                if (locationsChanged) lastLocations = locations;
                Main.Bridge?.Send(new WireFrame { kind = "state", batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, replaceJobs = true, jobs = lastJobs, replaceLocations = locationsChanged, locations = locationsChanged ? lastLocations : null } });
            }
            finally { jobsRunning = false; }
        }
        private JobState ReadJob(Job job, JobsManager manager, Dictionary<string, Tuple<string, string>> mapping)
        {
            var legs = new List<JobLeg>();
            for(int i=0;i<job.tasks.Count;i++)
                JobTaskCapture.Read(job.tasks[i],i.ToString(),job.State==DV.ThingTypes.JobState.InProgress,legs,ReadTask);
            double length = 0, mass = 0; bool massKnown = true;
            var ids = new List<string>(); var cargo = new HashSet<string>();
            bool runtimeUnavailable = false, onlyExplicitlySuspended = true, missingRuntime = false;
            int done = 0;
            foreach (var leg in legs) { if (leg.state == "Done") done++; foreach (var c in leg.cargo) cargo.Add(c); }
            bool active = job.State == DV.ThingTypes.JobState.InProgress;
            var all = JobCars(manager);
            var previous = lastJobs.FirstOrDefault(j => j.id == job.ID);
            HashSet<Car> jobCars = null;
            bool membershipKnown = all != null && all.TryGetValue(job, out jobCars) && jobCars != null;
            // A lost registry entry is different from a new generated job with
            // a known empty car set. Never publish a previously bound order as
            // ready/empty: that bypasses the stale/removal rules in CaptureJobs.
            if (!membershipKnown || jobCars.Count == 0 && previous?.cars?.Length > 0)
            {
                runtimeUnavailable = true; onlyExplicitlySuspended = false;
                missingRuntime = true; massKnown = false;
            }
            if (membershipKnown)
            foreach (var logicCar in jobCars)
                {
                    if (logicCar == null) { runtimeUnavailable = true; onlyExplicitlySuspended = false; missingRuntime = true; massKnown = false; continue; }
                    string stableId = PersistentJobRuntime.StableGuid(logicCar);
                    PersistentJobRuntime.TryResolve(logicCar, out var car);
                    if (car == null)
                    {
                        if (!string.IsNullOrEmpty(stableId) && IsPersistentSuspended(stableId))
                        {
                            ids.Add(stableId);
                            if (!mapping.ContainsKey(stableId)) mapping[stableId] = Tuple.Create(job.ID, job.chainData?.chainDestinationYardId);
                            runtimeUnavailable = true;
                            massKnown = false;
                            continue;
                        }
                        // A logical car that is absent from both the runtime
                        // registry and Persistent Jobs' suspended map has been
                        // deleted or otherwise lost. Do not resurrect its old
                        // GUID from lastJobs; retain the order visibly stale so
                        // native state remains the source of truth.
                        runtimeUnavailable = true;
                        onlyExplicitlySuspended = false;
                        missingRuntime = true;
                        massKnown = false;
                        continue;
                    }
                    ids.Add(car.CarGUID);
                    if (logicCar.CurrentCargoTypeInCar != CargoType.None && logicCar.LoadedCargoAmount > 0) cargo.Add(JobPresentation.CargoName(logicCar.CurrentCargoTypeInCar));
                    if ((active || job.State == DV.ThingTypes.JobState.Available) && (!mapping.TryGetValue(car.CarGUID, out var linked) || active && !sourceJobActive(linked.Item1))) mapping[car.CarGUID] = Tuple.Create(job.ID, job.chainData?.chainDestinationYardId);
                    length += car.InterCouplerDistance;
                    // A catalogue empty mass is not the current loaded mass.
                    // Keep the previous authoritative job total until the
                    // native TrainMassController has produced a live value.
                    if (car.massController != null && car.massController.TotalMass > 0 && !float.IsNaN(car.massController.TotalMass) && !float.IsInfinity(car.massController.TotalMass))
                        mass += car.massController.TotalMass;
                    else massKnown = false;
                }
            var licenses = new List<string>();
            foreach (JobLicenses license in Enum.GetValues(typeof(JobLicenses))) if ((int)license > 0 && (((int)license & ((int)license - 1)) == 0) && (job.requiredLicenses & license) == license) licenses.Add(license.ToString());
            double elapsed = active ? job.GetTimeOnJob() : job.State == DV.ThingTypes.JobState.Completed ? job.GetJobCompletionTime() : terminalJobs.TryGetValue(job, out var ended) ? ended : 0;
            string owner = null, ownerKey = null;
            string ownerStatus = job.State == DV.ThingTypes.JobState.Available || job.State == DV.ThingTypes.JobState.Expired ? "unassigned" : JobOwnership.ReadIdentity(job.ID, multiplayer.Mode, out owner, out ownerKey);
            jobStartDates.TryGetValue(job.ID, out var startedGameDate);
            if (runtimeUnavailable && onlyExplicitlySuspended && previous != null)
            {
                // A partial sum of the cars still in the runtime is not the
                // order's total. Keep the last complete total during suspend,
                // visibly qualified by dataQuality until the full set resumes.
                length = previous.length;
                if (!massKnown) { mass = previous.mass * 1000; massKnown = previous.massKnown; }
                foreach (var id in previous.cars ?? new string[0]) if (!ids.Contains(id)) ids.Add(id);
                foreach (var value in previous.cargo ?? new string[0]) cargo.Add(value);
            }
            if (!massKnown) mass = 0;
            var captured = new JobState { id = job.ID, type = job.jobType.ToString(), state = job.State.ToString(), active = active, owner = owner, ownerKey = ownerKey, ownerStatus = ownerStatus, startedGameDate = startedGameDate, origin = job.chainData?.chainOriginYardId, destination = job.chainData?.chainDestinationYardId, length = length, mass = mass / 1000, massKnown = massKnown && mass > 0, payment = job.GetBasePaymentForTheJob(), bonus = job.GetPotentialBonusPaymentForTheJob(), elapsedSeconds = Math.Max(0, elapsed), elapsedKnown = !missingRuntime && (active || job.State == DV.ThingTypes.JobState.Completed || terminalJobs.ContainsKey(job)), bonusLimitSeconds = job.TimeLimit + 60, sampledAt = Protocol.Now, sampledGameTime = manager.Time, tasksDone = done, tasksTotal = legs.Count, cars = ids.ToArray(), cargo = new List<string>(cargo).ToArray(), licenses = licenses.ToArray(), legs = legs.ToArray(), dataQuality = missingRuntime ? "stale" : runtimeUnavailable ? "suspended" : "ready" };
            JobPresentation.Apply(job.jobType, captured);
            if (passenger != null) {
                try { passenger.EnrichJob(job, captured); }
                catch (Exception e) { DropPassenger(e); captured.integrationStatus = "unavailable"; }
            }
            else if (!Enum.IsDefined(typeof(JobType), job.jobType)) captured.integrationStatus = passengerError ?? "unknown";
            return captured;
            bool sourceJobActive(string id) => manager.currentJobs.Any(j => j != null && j.ID == id && j.State == DV.ThingTypes.JobState.InProgress);
        }

        private JobLeg ReadTask(Task nativeTask, TaskData task)
        {
            string destination = task.destinationTrack?.ID?.FullDisplayID;
            var ids = new List<string>(); if (task.cars != null) foreach (var car in task.cars) {
                if (car == null) continue;
                PersistentJobRuntime.TryResolve(car, out var native);
                if(native!=null) ids.Add(native.CarGUID);
                else if (!string.IsNullOrEmpty(PersistentJobRuntime.StableGuid(car))) ids.Add(PersistentJobRuntime.StableGuid(car));
            }
            var cargo = new HashSet<string>(); if (task.cargoTypePerCar != null) foreach (var c in task.cargoTypePerCar) cargo.Add(JobPresentation.CargoName(c));
            string FindTrack(DV.Logic.Job.Track logic) { if (logic == null) return null; return TrackId(logic.RailTrack()); }
            var leg = new JobLeg { from = task.startTrack?.ID?.FullDisplayID ?? "", to = destination ?? "", fromTrack = FindTrack(task.startTrack), toTrack = FindTrack(task.destinationTrack), type = task.type.ToString(), state = task.state.ToString(), cars = ids.ToArray(), cargo = new List<string>(cargo).ToArray(), cargoAmount = task.totalCargoAmount, couplingRequired = task.couplingRequiredAndNotDone, handbrakeRequired = task.anyHandbrakeRequiredAndNotDone };
            leg.operation=task.warehouseTaskType.ToString();
            if (passenger != null) {
                try { passenger.EnrichTask(nativeTask, task, leg); }
                catch (Exception e) { DropPassenger(e); }
            }
            return leg;
        }
        private string RouteOrderState(RoutePlan plan) => string.IsNullOrEmpty(plan.jobId)?null:
            JobsManager.Instance==null?"JOB_TASK_UNAVAILABLE":JobTaskCapture.ValidateMovement(plan,
                JobsManager.Instance.currentJobs.Concat(JobCars(JobsManager.Instance)?.Keys.AsEnumerable()??Enumerable.Empty<Job>()),
                ReadTask, leg=>JobRouteRules.ShuntingConsistError(leg,LiveShuntingCars(),plan.trainCar??plan.train));
        private IEnumerable<CarState> LiveShuntingCars()
        {
            // A preview can outlive a coupling edit. Read the real couplers on
            // Unity's thread again before any native route is registered.
            foreach(var entry in carList) {
                var car=entry.car;if(car==null)continue;
                yield return new CarState {
                    id=entry.id, consist=car.trainset==null?"car:"+entry.id:"train:"+car.trainset.id,
                    nativeTrainset=car.trainset!=null, availability="available",
                    locomotive=car.IsLoco, vehicleCategory=VehiclePresentation.Category(car.carLivery),
                    couplersKnown=car.frontCoupler!=null&&car.rearCoupler!=null,
                    coupledFront=car.frontCoupler?.GetCoupled()?.train?.CarGUID,
                    coupledRear=car.rearCoupler?.GetCoupled()?.train?.CarGUID,
                    track1=TrackId(car.FrontBogie?.track),track2=TrackId(car.RearBogie?.track),
                    derailed=car.FrontBogie?.HasDerailed==true||car.RearBogie?.HasDerailed==true
                };
            }
        }
        private bool RouteTaskCompleted(RoutePlan plan) => !string.IsNullOrEmpty(plan.jobId) && !plan.passengerRoute && JobsManager.Instance!=null &&
            JobTaskCapture.MovementCompleted(plan,JobsManager.Instance.currentJobs.Concat(JobsManager.Instance.finishedJobs));
        [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.RegisterGeneratedJob))]
        private static class JobRegistered { private static void Postfix() => Main.Runtime?.RequestJobs(); }
        [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.UnregisterJob))]
        private static class JobUnregistered { private static void Postfix() => Main.Runtime?.RequestJobs(); }
        private static void BeforeJobChange(Job __instance, out DV.ThingTypes.JobState __state) => __state = __instance.State;
        [HarmonyPatch(typeof(Job), nameof(Job.TakeJob))]
        private static class JobTaken {
            private static void Prefix(Job __instance, out DV.ThingTypes.JobState __state) => BeforeJobChange(__instance, out __state);
            private static void Postfix(Job __instance, bool takenViaLoadGame, DV.ThingTypes.JobState __state) => Main.Runtime?.JobChanged(__instance, !takenViaLoadGame && __state != __instance.State);
        }
        [HarmonyPatch]
        private static class JobEnded {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
                foreach (var name in new[] { nameof(Job.CompleteJob), nameof(Job.AbandonJob), nameof(Job.ExpireJob) }) yield return AccessTools.Method(typeof(Job), name);
            }
            private static void Prefix(Job __instance, out DV.ThingTypes.JobState __state) => BeforeJobChange(__instance, out __state);
            private static void Postfix(Job __instance, DV.ThingTypes.JobState __state) => Main.Runtime?.JobChanged(__instance, __state != __instance.State);
        }
        [HarmonyPatch]
        private static class LocalJobValidation {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
                foreach (var name in new[] { nameof(JobValidator.ProcessJobOverview), nameof(JobValidator.ValidateJob) }) yield return AccessTools.Method(typeof(JobValidator), name);
            }
            private static void Prefix(out IDisposable __state) {
                __state = null;
                var runtime = Main.Runtime;
                if (JobActionContext.Current != null || runtime?.world != true || !runtime.multiplayer.Authority) return;
                var player = runtime.multiplayer.CapturePlayers().FirstOrDefault(p => p.host);
                if (!string.IsNullOrEmpty(player.id)) __state = new JobActionContext(player.id, player.name);
            }
            private static void Finalizer(IDisposable __state) => __state?.Dispose();
        }
    }
}
