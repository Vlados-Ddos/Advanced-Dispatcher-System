using DV.Logic.Job;
using System.Collections.Generic;

namespace AdvancedDispatcherSystem.Game
{
    /// <summary>
    /// Resolves the optional Persistent Jobs logical-car reference without
    /// invoking LogicCarExtensions.TrainCar. That extension is a dictionary
    /// indexer and throws KeyNotFoundException when Persistent Jobs has
    /// deleted the runtime object. A missing registry entry is a normal
    /// suspended lifecycle state and is represented by false.
    /// </summary>
    internal static class PersistentJobRuntime
    {
        internal static string StableGuid(Car logicCar) => logicCar?.carGuid;

        internal static bool TryResolve(Car logicCar, out TrainCar trainCar)
        {
            trainCar = null;
            if (logicCar == null) return false;
            var registry = TrainCarRegistry.Instance;
            return TryResolve(registry?.logicCarToTrainCar, logicCar, out trainCar);
        }

        internal static bool TryResolve(IDictionary<Car, TrainCar> map, Car logicCar, out TrainCar trainCar)
        {
            trainCar = null;
            if (logicCar == null || map == null) return false;
            if (!map.TryGetValue(logicCar, out trainCar) || trainCar == null)
            {
                trainCar = null;
                return false;
            }
            return true;
        }
    }
}
