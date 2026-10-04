using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdvancedDispatcherSystem.Core;
using DV.Logic.Job;
using DV.LocoRestoration;
using DV.RemoteControls;
using DV.ThingTypes;
using UnityEngine;
using JobState = AdvancedDispatcherSystem.Core.JobState;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private sealed class CarEntry
        {
            public TrainCar car;
            public RemoteControllerModule remote;
            public ILocomotiveRemoteControl controls;
            public string id, type, consist;
            public int consistNumber = -1;
            public float nextLookup;
        }
        private readonly Dictionary<string, CarEntry> carEntries = new Dictionary<string, CarEntry>();
        private readonly List<CarEntry> carList = new List<CarEntry>();
        // One authoritative consist sum per capture sweep; without this cache
        // every wagon would walk the same Trainset.cars collection again.
        private readonly Dictionary<string, double> consistMassCache = new Dictionary<string, double>();
        private readonly Dictionary<string, Tuple<string, string>> carJobs = new Dictionary<string, Tuple<string, string>>();
        private JobState[] lastJobs = new JobState[0];
        private bool jobsRunning;
        private System.Reflection.EventInfo persistentJobEvent;
        private Action<Job> persistentJobHandler;
        private readonly List<Tuple<EventInfo, Delegate>> persistentJobHooks = new List<Tuple<EventInfo, Delegate>>();
        private Type persistentFarCarType;
        private FieldInfo persistentSuspendedById;
        private FieldInfo persistentSuspendedByGuid;
        private bool loggedPersistentSuspendLookupFailure;
        private void HookJobs()
        {
            try
            {
                var assembly = UnityModManagerNet.UnityModManager.FindMod("PersistentJobsMod")?.Assembly;
                var interaction = assembly?.GetType("PersistentJobsMod.ModInteraction.PersistentJobsModInteractionFeatures") ?? assembly?.GetType("PersistentJobsModInteractionFeatures");
                persistentJobEvent = interaction?.GetEvent("JobTracksChanged");
                if (persistentJobEvent != null) { persistentJobHandler = PersistentJobTracksChanged; persistentJobEvent.AddEventHandler(null, persistentJobHandler); persistentJobHooks.Add(Tuple.Create(persistentJobEvent, (Delegate)persistentJobHandler)); }
                AddPersistentHook(interaction, "JobCarsChanged", nameof(PersistentJobCarsChanged));
                persistentFarCarType = assembly?.GetType("PersistentJobsMod.Optimization.FarCarOpt");
                persistentSuspendedById = persistentFarCarType?.GetField("SuspendedCarIDToCarGUID", BindingFlags.Public | BindingFlags.Static);
                persistentSuspendedByGuid = persistentFarCarType?.GetField("SuspendedCarGUIDToCarID", BindingFlags.Public | BindingFlags.Static);
                AddPersistentHook(persistentFarCarType, "SuspendCompleted", nameof(PersistentSuspendCompleted));
                AddPersistentHook(persistentFarCarType, "ResumeCompleted", nameof(PersistentResumeCompleted));
            }
            catch (Exception e) { Main.Log("PERSISTENT_JOBS_ADAPTER_FAILED", e); }
        }
        private void AddPersistentHook(Type type, string eventName, string methodName)
        {
            if (type == null) return;
            var evt = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
            var method = GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (evt == null || method == null) return;
            var handler = Delegate.CreateDelegate(evt.EventHandlerType, this, method, true);
            evt.AddEventHandler(null, handler); persistentJobHooks.Add(Tuple.Create(evt, handler));
        }
        private void PersistentJobTracksChanged(Job _) => RequestJobs();
        private void PersistentJobCarsChanged((Job job, Car car) _) => RequestJobs();
        private void PersistentSuspendCompleted() => RequestJobs();
        private void PersistentResumeCompleted(string _) { RebindCars(); RequestJobs(); }
        private void UnhookJobs()
        {
            foreach (var hook in persistentJobHooks.ToArray())
                try { hook.Item1.RemoveEventHandler(null, hook.Item2); }
                catch (Exception e) { Main.Log("PERSISTENT_JOBS_UNHOOK_FAILED", e); }
            persistentJobHooks.Clear(); persistentJobEvent = null; persistentJobHandler = null;
            persistentFarCarType = null; persistentSuspendedById = null; persistentSuspendedByGuid = null;
        }
        private long jobsRevision;
        private void RequestJobs()
        {
            jobsRevision++; nextJobs = 0;
            // A job event can invalidate an in-progress capture (including a
            // Persistent Jobs suspend/resume). Publish the last complete
            // snapshot as explicitly stale immediately, so the Host/UI cannot
            // keep offering a route action against changed native task data.
            // Clone before queuing: the bridge serializes on another thread.
            if (!world || lastJobs == null || lastJobs.Length == 0 ||
                !lastJobs.Any(job => job != null && (job.dataQuality != "stale" || job.elapsedKnown))) return;
            lastJobs = JobSnapshotQuality.MarkStale(lastJobs);
            Main.Bridge?.Send(new WireFrame
            {
                kind = "state",
                batch = new GameBatch { epoch = epoch, topologyRevision = topologyRevision, replaceJobs = true, jobs = lastJobs }
            });
        }
        private static readonly System.Reflection.FieldInfo JobCarsField = typeof(JobsManager).GetField("jobToJobCars", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static Dictionary<Job, HashSet<Car>> JobCars(JobsManager manager) => JobCarsField?.GetValue(manager) as Dictionary<Job, HashSet<Car>>;
        private void SeedCars()
        {
            carEntries.Clear(); carList.Clear(); VehiclePresentation.Reset();
            DisplayText.ResetSearchNames();
            VehiclePresentation.CaptureCatalogue();
            var registry = TrainCarRegistry.Instance;
            if (registry != null) foreach (var car in registry.logicCarToTrainCar.Values) CarAdded(car);
        }
        private void CarAdded(TrainCar car)
        {
            if (car == null || string.IsNullOrEmpty(car.CarGUID)) return;
            if (carEntries.TryGetValue(car.CarGUID, out var restored))
            {
                restored.car = car; restored.remote = null; restored.controls = null; restored.nextLookup = 0;
                return;
            }
            var entry = new CarEntry { car = car, id = car.CarGUID, type = car.carType.ToString() };
            carEntries.Add(entry.id, entry); carList.Add(entry);
        }
        private void CarRemoved(TrainCar car)
        {
            if (car == null || !carEntries.TryGetValue(car.CarGUID, out var entry)) return;
            if (IsPersistentSuspended(car.CarGUID))
            {
                // Persistent Jobs deliberately destroys the runtime TrainCar.
                // Keep the stable GUID and the last sampled state, but release
                // every managed/native object reference until CarSpawned binds
                // the replacement instance after Resume.
                entry.car = null; entry.remote = null; entry.controls = null;
                changedCars.RemoveAll(value => value.id == entry.id);
                changedMotions.RemoveAll(value => value.id == entry.id);
                MarkCarUnavailable(entry.id);
                RequestJobs();
                return;
            }
            carEntries.Remove(entry.id); carList.Remove(entry);
            if (carStates.Remove(entry.id)) removedCars.Add(entry.id);
            carJobs.Remove(entry.id); changedCars.RemoveAll(value => value.id == entry.id); changedMotions.RemoveAll(value => value.id == entry.id);
            // JobsCapture may still contain the removed GUID until its next
            // sweep. Invalidate that snapshot immediately so the Host cannot
            // offer a route against a deleted runtime car.
            RequestJobs();
        }
        private bool IsPersistentSuspended(string carGuid)
        {
            if (string.IsNullOrEmpty(carGuid) || persistentSuspendedByGuid == null) return false;
            try { return (persistentSuspendedByGuid.GetValue(null) as System.Collections.IDictionary)?.Contains(carGuid) == true; }
            catch (Exception e)
            {
                // A destroyed/recreated Persistent Jobs singleton can make the
                // reflected lookup temporarily invalid. Keep the fallback
                // explicit and bounded: diagnose once, then let the normal
                // removal path handle the object instead of hiding the fault
                // behind a retry/exception loop.
                if (!loggedPersistentSuspendLookupFailure)
                {
                    loggedPersistentSuspendLookupFailure = true;
                    Main.Log("PERSISTENT_JOBS_SUSPEND_LOOKUP_FAILED", e);
                }
                return false;
            }
        }
        private void MarkCarUnavailable(string id)
        {
            if (!carStates.TryGetValue(id, out var old)) return;
            if (old.availability == "suspended") return;
            old.availability = "suspended"; old.revision = ++entityRevision; old.sampledAt = Protocol.Now; carStates[id] = old; changedCars.Add(old);
        }
        private void RebindCars()
        {
            var registry = TrainCarRegistry.Instance;
            if (registry == null) return;
            foreach (var car in registry.logicCarToTrainCar.Values) CarAdded(car);
        }
        private static double CarMassKg(TrainCar car)
        {
            if (car == null) return double.NaN;
            try
            {
                if (car.massController != null && car.massController.TotalMass > 0 && !float.IsNaN(car.massController.TotalMass))
                    return car.massController.TotalMass;
                // A type's empty mass is not a substitute for the live mass:
                // loaded cargo/resources can change it. Wait for the native
                // TrainMassController instead of publishing a guessed value.
                return double.NaN;
            }
            catch { return double.NaN; }
        }
        private static double CurrentTractionN(TrainCar car)
        {
            try
            {
                var force = car?.SimController?.drivingForce?.generatedForce ?? float.NaN;
                return float.IsNaN(force) || float.IsInfinity(force) ? double.NaN : Math.Abs(force);
            }
            catch { return double.NaN; }
        }
        private double ConsistMassKg(TrainCar car)
        {
            if (car == null) return double.NaN;
            try
            {
                var set = car.trainset;
                if (set == null || set.cars == null || set.cars.Count == 0) return CarMassKg(car);
                var key = "train:" + set.id;
                if (consistMassCache.TryGetValue(key, out var cached)) return cached;
                double total = 0; bool known = true;
                foreach (var member in set.cars)
                {
                    var mass = CarMassKg(member);
                    if (double.IsNaN(mass) || double.IsInfinity(mass)) known = false;
                    else total += mass;
                }
                var result = known && total > 0 ? total : double.NaN;
                consistMassCache[key] = result;
                return result;
            }
            catch { return double.NaN; }
        }
        private void SampleCar(CarEntry entry)
        {
            var car = entry.car;
            if (car == null)
            {
                if (IsPersistentSuspended(entry.id)) MarkCarUnavailable(entry.id);
                return;
            }
            if (!ShowUndiscovered)
            {
                var restoration = LocoRestorationController.GetForTrainCar(car);
                if (restoration != null && restoration.State < LocoRestorationController.RestorationState.S3_RerailedCars)
                { if (carStates.Remove(entry.id)) removedCars.Add(entry.id); return; }
            }
            if (car.IsLoco && entry.remote == null && Time.realtimeSinceStartup >= entry.nextLookup)
            { entry.nextLookup = Time.realtimeSinceStartup + 2; entry.remote = car.GetComponent<RemoteControllerModule>(); entry.controls = car.GetComponent<ILocomotiveRemoteControl>(); }
            var p = car.transform.TransformPoint(car.Bounds.center) - WorldMover.currentMove;
            var first = car.FrontBogie; var last = car.RearBogie;
            int set = car.trainset == null ? -1 : car.trainset.id;
            if (entry.consistNumber != set || entry.consist == null) { entry.consistNumber = set; entry.consist = set < 0 ? "car:" + entry.id : "train:" + set; }
            carJobs.TryGetValue(entry.id, out var job);
            float speed = car.GetForwardSpeed();
            double massKg = CarMassKg(car), consistMassKg = ConsistMassKg(car);
            double tractionN = CurrentTractionN(car);
            carStates.TryGetValue(entry.id, out var previousSample);
            var value = new CarState
            {
                id = entry.id,
                name = car.ID,
                type = entry.type,
                model = VehiclePresentation.Model(car.carLivery), modelLanguage = Main.LanguageCode,
                catalogModel = VehiclePresentation.CatalogModel(car.carLivery),
                searchNames = DisplayText.SearchNames(car.carLivery?.parentType?.localizationKey),
                catalogColor = VehiclePresentation.CatalogColor(car.carLivery),
                consist = entry.consist,
                nativeTrainset = car.trainset != null,
                couplersKnown = car.frontCoupler != null && car.rearCoupler != null,
                coupledFront = car.frontCoupler?.GetCoupled()?.train?.CarGUID,
                coupledRear = car.rearCoupler?.GetCoupled()?.train?.CarGUID,
                vehicleCategory = VehiclePresentation.Category(car.carLivery),
                order = car.indexInTrainset,
                x = Math.Round(p.x, 2),
                z = Math.Round(p.z, 2),
                yaw = Math.Round(car.transform.eulerAngles.y, 1),
                length = car.InterCouplerDistance, width = car.Bounds.size.x,
                massKnown = !double.IsNaN(massKg) && !double.IsInfinity(massKg) && massKg > 0,
                mass = !double.IsNaN(massKg) && !double.IsInfinity(massKg) ? massKg / 1000d : 0,
                consistMass = !double.IsNaN(consistMassKg) && !double.IsInfinity(consistMassKg) ? consistMassKg / 1000d : 0,
                tractionKnown = !double.IsNaN(tractionN) && !double.IsInfinity(tractionN),
                availableTraction = !double.IsNaN(tractionN) && !double.IsInfinity(tractionN) ? tractionN : 0,
                track1 = first == null ? null : TrackId(first.track),
                track2 = last == null ? null : TrackId(last.track),
                span1 = first?.traveller == null ? 0 : first.traveller.Span,
                span2 = last?.traveller == null ? 0 : last.traveller.Span,
                speed = Math.Round(speed * 3.6, 2),
                direction = Math.Abs(speed) < 0.05f || first == null ? 0 : Math.Sign(speed * first.TrackDirectionSign),
                consistCargo = car.IsLoco ? ConsistCargo.Capture(car, previousSample.consistCargo) : null,
                cargoKnown = car.logicCar != null,
                cargo = car.logicCar == null ? null : JobPresentation.CargoName(car.LoadedCargo),
                cargoAmount = car.logicCar == null ? 0 : car.LoadedCargoAmount,
                locomotive = car.IsLoco,
                derailed = first != null && first.HasDerailed || last != null && last.HasDerailed,
                controllable = entry.remote != null && entry.controls != null && car.SimController?.controlsOverrider != null,
                sampledAt = Protocol.Now,
                availability = "available",
                job = job?.Item1,
                destination = job?.Item2
            };
            if (value.controllable)
            {
                value.throttle = entry.controls.GetTargetThrottle(); value.trainBrake = entry.controls.GetTargetBrake(); value.independentBrake = entry.controls.GetTargetIndependentBrake(); value.reverser = entry.controls.GetReverserValue();
                value.brakePipe = car.brakeSystem.brakePipePressure; value.slipping = entry.controls.IsWheelslipping(); value.canCouple = entry.controls.IsCouplerInRange(ExternalCouplingHandler.COUPLING_RANGE);
                value.frontCount = entry.controls.GetNumberOfCarsInFront(); value.rearCount = entry.controls.GetNumberOfCarsInRear();
            }
            bool existed = carStates.TryGetValue(entry.id, out var old);
            if (existed && SameCar(old, value)) return;
            value.revision = existed ? old.revision : ++entityRevision; carStates[entry.id] = value;
            if (existed && SameMetadata(old, value)) changedMotions.Add(CarMotion.From(value));
            else { value.revision = ++entityRevision; carStates[entry.id] = value; changedCars.Add(value); }
        }
        private static bool SameMetadata(CarState a, CarState b) => a.availability == b.availability && a.tractionKnown == b.tractionKnown && a.availableTraction == b.availableTraction && a.massKnown == b.massKnown && a.mass == b.mass && a.consistMass == b.consistMass && ReferenceEquals(a.searchNames,b.searchNames) && a.couplersKnown == b.couplersKnown && a.coupledFront == b.coupledFront && a.coupledRear == b.coupledRear && a.vehicleCategory == b.vehicleCategory && a.nativeTrainset == b.nativeTrainset && ConsistCargo.Same(a.consistCargo,b.consistCargo) && a.model == b.model && a.modelLanguage == b.modelLanguage && a.catalogModel == b.catalogModel && a.catalogColor == b.catalogColor && a.length == b.length && a.width == b.width && a.cargoKnown == b.cargoKnown && a.cargo == b.cargo && a.cargoAmount == b.cargoAmount && a.name == b.name && a.consist == b.consist && a.order == b.order && a.controllable == b.controllable && a.job == b.job && a.destination == b.destination && a.throttle == b.throttle && a.trainBrake == b.trainBrake && a.independentBrake == b.independentBrake && a.reverser == b.reverser && Math.Abs(a.brakePipe - b.brakePipe) < 0.02 && a.slipping == b.slipping && a.canCouple == b.canCouple && a.frontCount == b.frontCount && a.rearCount == b.rearCount;
        private static bool SameCar(CarState a, CarState b) => a.availability == b.availability && a.tractionKnown == b.tractionKnown && a.availableTraction == b.availableTraction && a.massKnown == b.massKnown && a.mass == b.mass && a.consistMass == b.consistMass && a.couplersKnown == b.couplersKnown && a.coupledFront == b.coupledFront && a.coupledRear == b.coupledRear && a.vehicleCategory == b.vehicleCategory && a.nativeTrainset == b.nativeTrainset && ConsistCargo.Same(a.consistCargo,b.consistCargo) && a.model == b.model && a.modelLanguage == b.modelLanguage && a.catalogModel == b.catalogModel && a.catalogColor == b.catalogColor && a.length == b.length && a.width == b.width && a.cargoKnown == b.cargoKnown && a.cargo == b.cargo && a.cargoAmount == b.cargoAmount && a.name == b.name && a.consist == b.consist && a.order == b.order && a.track1 == b.track1 && a.track2 == b.track2 && a.x == b.x && a.z == b.z && a.yaw == b.yaw && a.speed == b.speed && a.derailed == b.derailed && a.controllable == b.controllable && a.job == b.job && a.destination == b.destination && a.direction == b.direction && a.throttle == b.throttle && a.trainBrake == b.trainBrake && a.independentBrake == b.independentBrake && a.reverser == b.reverser && Math.Abs(a.brakePipe - b.brakePipe) < 0.02 && a.slipping == b.slipping && a.canCouple == b.canCouple && a.frontCount == b.frontCount && a.rearCount == b.rearCount;
        private CommandResult LocoCommand(Command command)
        {
            CommandResult Error(string code) => new CommandResult { id = command.id, status = "rejected", code = code };
            if (!carEntries.TryGetValue(command.target ?? "", out var entry) || entry.car == null || entry.remote == null || entry.car.SimController?.controlsOverrider == null) return Error("NOT_CONTROLLABLE");
            if (float.IsNaN(command.value) || float.IsInfinity(command.value)) return Error("INVALID_VALUE");
            var control = entry.car.SimController.controlsOverrider;
            if (command.action == "couple")
            {
                // Recheck the game's own whole-consist coupling predicate at
                // execution time, never infer it from speed or attached cars.
                if (entry.controls == null || !entry.controls.IsCouplerInRange(ExternalCouplingHandler.COUPLING_RANGE)) return Error("COUPLING_UNAVAILABLE");
                entry.remote.RemoteControllerCouple();
            }
            else if (command.action == "uncouple")
            {
                int i = command.index;
                if (i == 0 || entry.controls == null || i > entry.controls.GetNumberOfCarsInFront() || i < -entry.controls.GetNumberOfCarsInRear()) return Error("INVALID_VALUE");
                entry.remote.Uncouple(i);
            }
            else
            {
                if (command.value < 0 || command.value > 1) return Error("INVALID_VALUE");
                switch (command.action)
                {
                    case "throttle": if (control.Throttle == null) return Error("NOT_CONTROLLABLE"); control.Throttle.Set(command.value); break;
                    case "trainBrake": if (control.Brake == null) return Error("NOT_CONTROLLABLE"); control.Brake.Set(command.value); break;
                    case "independentBrake": if (control.IndependentBrake == null) return Error("NOT_CONTROLLABLE"); control.IndependentBrake.Set(command.value); break;
                    case "reverser": if (control.Reverser == null) return Error("NOT_CONTROLLABLE"); control.Reverser.Set(command.value); break;
                    default: return Error("INVALID_COMMAND");
                }
            }
            SampleCar(entry);
            return new CommandResult { id = command.id, status = "applied", target = command.target, warnings = multiplayer.Mode == "host" ? new[] { "SHARED_LOCO_CONTROL" } : new string[0] };
        }
    }
}
