using AdvancedDispatcherSystem.Core;
using DV.ThingTypes;
using UnityEngine;
using JobState = AdvancedDispatcherSystem.Core.JobState;

namespace AdvancedDispatcherSystem.Game
{
    internal static class JobPresentation
    {
        public static string CargoName(CargoType cargo)
        {
            string name = cargo.ToString();
            if (name.Length == 0 || !char.IsDigit(name[0])) return name;
            // Optional mods register real CargoType_v2 IDs; Enum.ToString does not.
            var types = DV.Globals.G.Types;
            if (types != null) foreach (var entry in types.cargos)
                if (entry != null && entry.v1 == cargo && !string.IsNullOrEmpty(entry.id)) return entry.id;
            return name;
        }
        public static void Apply(JobType type, JobState state)
        {
            Color? color = null;
            switch (type)
            {
                case JobType.Transport: color = DV.Booklets.C.HAUL_JOB_TYPE_COLOR; break;
                case JobType.ShuntingLoad: color = DV.Booklets.C.SHUNTING_LOAD_JOB_TYPE_COLOR; break;
                case JobType.ShuntingUnload: color = DV.Booklets.C.SHUNTING_UNLOAD_JOB_TYPE_COLOR; break;
                case JobType.EmptyHaul: color = DV.Booklets.C.EMPTY_HAUL_JOB_TYPE_COLOR; break;
            }
            state.typeColor = color.HasValue ? "#" + ColorUtility.ToHtmlStringRGB(color.Value) : null;
            state.typeSource = color.HasValue ? "Derail Valley" : null;
            state.integrationStatus = "ready";
        }
    }
}
