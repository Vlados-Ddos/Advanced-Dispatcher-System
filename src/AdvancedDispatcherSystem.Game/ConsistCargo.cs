using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using DV.ThingTypes;

namespace AdvancedDispatcherSystem.Game
{
    internal static class ConsistCargo
    {
        private sealed class Buffer {
            public readonly HashSet<TrainCar> seen = new HashSet<TrainCar>();
            public readonly Stack<TrainCar> pending = new Stack<TrainCar>();
            public readonly HashSet<string> cargo = new HashSet<string>(StringComparer.Ordinal), members = new HashSet<string>(StringComparer.Ordinal);
            public void Clear() { seen.Clear(); pending.Clear(); cargo.Clear(); members.Clear(); }
        }
        [ThreadStatic] private static Buffer buffer;
        private static string[] Snapshot(HashSet<string> values, string[] previous) {
            if (previous != null && values.SetEquals(previous)) return previous;
            var result = values.ToArray(); Array.Sort(result, StringComparer.Ordinal); return result;
        }
        // GetCoupled is the same physical chain used by native remote coupling.
        // Inspect it in one Unity-thread sample, independent of the web car scan.
        public static CargoSummary Capture(TrainCar selected, CargoSummary previous = null)
        {
            var result = new CargoSummary { complete = true, lengthKnown = true, membershipComplete = true };
            var work = buffer ?? (buffer = new Buffer());
            var seen = work.seen; var pending = work.pending;
            var cargo = work.cargo; var members = work.members;
            try {
            void Incomplete() { result.complete = result.lengthKnown = result.membershipComplete = false; }
            if (selected == null) { Incomplete(); return result; }
            pending.Push(selected);
            while (pending.Count > 0) {
                var car = pending.Pop();
                if (car == null) { Incomplete(); continue; }
                if (!seen.Add(car)) continue;
                result.cars++;
                if (string.IsNullOrEmpty(car.CarGUID) || !members.Add(car.CarGUID)) result.membershipComplete = false;
                double length = car.InterCouplerDistance;
                if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0) result.lengthKnown = false;
                else result.length += length;
                if (car.logicCar == null) result.complete = false;
                else {
                    var amount = car.logicCar.LoadedCargoAmount;
                    var type = car.logicCar.CurrentCargoTypeInCar;
                    if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0 || amount > 0 && type == CargoType.None) result.complete = false;
                    else if (amount > 0) cargo.Add(JobPresentation.CargoName(type));
                }
                Visit(car.frontCoupler); Visit(car.rearCoupler);
            }
            result.types = Snapshot(cargo, previous?.types); result.members = Snapshot(members, previous?.members);
            return Same(result, previous) ? previous : result;
            void Visit(Coupler coupler) {
                if (coupler == null) { Incomplete(); return; }
                var other = coupler.GetCoupled();
                if (other == null) return;
                if (other.train == null || other.GetCoupled() != coupler) { Incomplete(); return; }
                pending.Push(other.train);
            }
            } finally { work.Clear(); }
        }
        public static bool Same(CargoSummary a, CargoSummary b) => ReferenceEquals(a,b) ||
            a != null && b != null && a.complete == b.complete && a.cars == b.cars && a.types.SequenceEqual(b.types) &&
            a.lengthKnown == b.lengthKnown && a.membershipComplete == b.membershipComplete && a.length == b.length && a.members.SequenceEqual(b.members);
    }
}
