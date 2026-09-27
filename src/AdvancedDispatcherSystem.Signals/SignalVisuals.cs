using System;
using DVSignal = global::Signals.Game.Signal;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        internal static bool SameControlState(AdvancedDispatcherSystem.Core.SignalState old, int aspect,
            int overrideIndex, string mode, bool shunting, bool reserved) =>
            old != null && old.aspectIndex == aspect && old.overrideIndex == overrideIndex &&
            old.mode == mode && old.shunting == shunting && old.reserved == reserved;
        // Verified metadata from DV Signals 1.1.3's signal_bundle. Only semantic
        // descriptors cross IPC: no texture access, sprite extraction or graphics
        // dependency. Unknown packs retain their actual lamp layout.
        internal static string MechanicalKind(string off) =>
            off == "UI_SM1_off" ? "semaphore1" :
            off == "UI_SM2_off" ? "semaphore2" :
            off == "UI_SS_stop" ? "discShunting" :
            off == "UI_SD_next_stop" ? "discDistant" : null;

        internal static string MechanicalState(string off, string current, bool isOff)
        {
            if (MechanicalKind(off) == null) return null;
            if (isOff) return "off";
            string prefix = off.Substring(0, off.LastIndexOf('_') + 1);
            if (off == "UI_SD_next_stop") prefix = "UI_SD_";
            if (current == prefix + "clear") return "clear";
            if (current == prefix + "stop" || current == prefix + "next_stop") return "stop";
            if (off == "UI_SM2_off" && current == "UI_SM2_restricted") return "restricted";
            return "unknown";
        }

        private static string VisualState(DVSignal signal) =>
            MechanicalState(signal.Definition.OffStateHUDSprite?.name,
                signal.CurrentAspect?.GetDefinition()?.HUDSprite?.name, signal.IsOff);

        internal static string BoardKind(string resource) =>
            resource == "UI_Distant_Board" || resource == "UI_Distant_Board_wide" ? "distantBoard" :
            resource == "UI_Distant_Short" ? "distantShort" :
            resource == "UI_ShuntEnd" ? "shuntLimit" : "fixedBoard";
    }
}
