using System;
using System.Collections.Generic;
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
        private readonly Dictionary<string, Tuple<string, string>> carJobs = new Dictionary<string, Tuple<string, string>>();
        private JobState[] lastJobs = new JobState[0];
        private bool jobsRunning;
        private System.Reflection.EventInfo persistentJobEvent;
        private Action<Job> persistentJobHandler;
        private void HookJobs()
        {
            try
            {
                persistentJobEvent = UnityModManagerNet.UnityModManager.FindMod("PersistentJobsMod")?.Assembly?.GetType("PersistentJobsModInteractionFeatures")?.GetEvent("JobTracksChanged");
                if (persistentJobEvent != null) { persistentJobHandler = job => RequestJobs(); persistentJobEvent.AddEventHandler(null, persistentJobHandler); }
            }
            catch (Exception e) { Main.Log("PERSISTENT_JOBS_ADAPTER_FAILED", e); }
        }
        private void UnhookJobs() { if (persistentJobEvent != null && persistentJobHandler != null) persistentJobEvent.RemoveEventHandler(null, persistentJobHandler); persistentJobEvent = null; persistentJobHandler = null; }
        private long jobsRevision;
        private void RequestJobs() { jobsRevision++; nextJobs = 0; }
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
            if (car == null || string.IsNullOrEmpty(car.CarGUID) || carEntries.ContainsKey(car.CarGUID)) return;
            var entry = new CarEntry { car = car, id = car.CarGUID, type = car.carType.ToString() };
            carEntries.Add(entry.id, entry); carList.Add(entry);
        }
        private void CarRemoved(TrainCar car)
        {
            if (car == null || !carEntries.TryGetValue(car.CarGUID, out var entry)) return;
            carEntries.Remove(entry.id); carList.Remove(entry);
            if (carStates.Remove(entry.id)) removedCars.Add(entry.id);
            carJobs.Remove(entry.id); changedCars.RemoveAll(value => value.id == entry.id); changedMotions.RemoveAll(value => value.id == entry.id);
        }
        private void SampleCar(CarEntry entry)
        {
            var car = entry.car; if (car == null) return;
            if (!Main.Config.ShowUndiscovered)
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
            carStates.TryGetValue(entry.id, out var previousSample);
            var value = new CarState
            {
                id = entry.id,
                name = car.ID,
                type = entry.type,
                model = VehiclePresentation.Model(car.carLivery), modelLanguage = Main.LanguageCode,
                searchNames = DisplayText.SearchNames(car.carLivery?.parentType?.localizationKey),
                catalogColor = VehiclePresentation.CatalogColor(car.carLivery),
                consist = entry.consist,
                nativeTrainset = car.trainset != null,
                vehicleCategory = VehiclePresentation.Category(car.carLivery),
                order = car.indexInTrainset,
                x = Math.Round(p.x, 2),
                z = Math.Round(p.z, 2),
                yaw = Math.Round(car.transform.eulerAngles.y, 1),
                length = car.InterCouplerDistance, width = car.Bounds.size.x,
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
        private static bool SameMetadata(CarState a, CarState b) => ReferenceEquals(a.searchNames,b.searchNames) && a.vehicleCategory == b.vehicleCategory && a.nativeTrainset == b.nativeTrainset && ConsistCargo.Same(a.consistCargo,b.consistCargo) && a.model == b.model && a.modelLanguage == b.modelLanguage && a.catalogColor == b.catalogColor && a.length == b.length && a.width == b.width && a.cargoKnown == b.cargoKnown && a.cargo == b.cargo && a.cargoAmount == b.cargoAmount && a.name == b.name && a.consist == b.consist && a.order == b.order && a.controllable == b.controllable && a.job == b.job && a.destination == b.destination && a.throttle == b.throttle && a.trainBrake == b.trainBrake && a.independentBrake == b.independentBrake && a.reverser == b.reverser && Math.Abs(a.brakePipe - b.brakePipe) < 0.02 && a.slipping == b.slipping && a.canCouple == b.canCouple && a.frontCount == b.frontCount && a.rearCount == b.rearCount;
        private static bool SameCar(CarState a, CarState b) => a.vehicleCategory == b.vehicleCategory && a.nativeTrainset == b.nativeTrainset && ConsistCargo.Same(a.consistCargo,b.consistCargo) && a.model == b.model && a.modelLanguage == b.modelLanguage && a.catalogColor == b.catalogColor && a.length == b.length && a.width == b.width && a.cargoKnown == b.cargoKnown && a.cargo == b.cargo && a.cargoAmount == b.cargoAmount && a.name == b.name && a.consist == b.consist && a.order == b.order && a.track1 == b.track1 && a.track2 == b.track2 && a.x == b.x && a.z == b.z && a.yaw == b.yaw && a.speed == b.speed && a.derailed == b.derailed && a.controllable == b.controllable && a.job == b.job && a.destination == b.destination && a.direction == b.direction && a.throttle == b.throttle && a.trainBrake == b.trainBrake && a.independentBrake == b.independentBrake && a.reverser == b.reverser && Math.Abs(a.brakePipe - b.brakePipe) < 0.02 && a.slipping == b.slipping && a.canCouple == b.canCouple && a.frontCount == b.frontCount && a.rearCount == b.rearCount;
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
