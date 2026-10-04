using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using DVSignal = global::Signals.Game.Signal;

namespace AdvancedDispatcherSystem.Signals
{
    public sealed partial class Adapter
    {
        private const string SynchronizationPatchId = "denis.ads.signals-manual-sync";
        private Harmony synchronizationHarmony;
        private Assembly synchronizationAssembly;
        private float nextSynchronizationCheck;

        private void EnsureSignalSynchronization()
        {
            if (synchronizationHarmony != null || UnityEngine.Time.realtimeSinceStartup < nextSynchronizationCheck) return;
            nextSynchronizationCheck = UnityEngine.Time.realtimeSinceStartup + 2;
            // Signals.MP is optional and may load after the capture adapter.
            // Installation and removal run on the Unity thread, not AssemblyLoad.
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Signals.MP");
            if (assembly == null || assembly == synchronizationAssembly) return;
            synchronizationAssembly = assembly;
            try {
                InstallSignalSynchronization(assembly);
                UnityEngine.Debug.Log("ADS SIGNAL_MANUAL_SYNC_READY packet=ManualOverrideAspect replay=mode+override+shunting");
            }
            catch (Exception e) { UnityEngine.Debug.LogError("ADS SIGNAL_MANUAL_SYNC_UNAVAILABLE " + e); }
        }

        private void InstallSignalSynchronization(Assembly assembly)
        {
            if (synchronizationHarmony != null) return;
            var factory = assembly.GetType("Signals.MP.OverridePacket")?.GetMethod("FromSignal", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(DVSignal) }, null);
            var replay = assembly.GetType("Signals.MP.ServerManager")?.GetMethod("PlayerReady", BindingFlags.Instance | BindingFlags.NonPublic);
            if (factory == null || replay == null) throw new MissingMethodException("DV Signals override/replay contract");
            var patch = new Harmony(SynchronizationPatchId);
            try
            {
                patch.Patch(factory, transpiler: new HarmonyMethod(typeof(Adapter), nameof(UseStoredOverride)));
                patch.Patch(replay, transpiler: new HarmonyMethod(typeof(Adapter), nameof(ReplayControlState)));
                synchronizationHarmony = patch;
            }
            catch { patch.UnpatchAll(SynchronizationPatchId); throw; }
        }

        private static IEnumerable<CodeInstruction> UseStoredOverride(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var displayed = AccessTools.PropertyGetter(typeof(DVSignal), nameof(DVSignal.CurrentAspectIndex));
            var stored = AccessTools.PropertyGetter(typeof(DVSignal), nameof(DVSignal.ManualOverrideAspect));
            var reads = code.Where(i => i.Calls(displayed)).ToArray();
            if (reads.Length == 0 && code.Count(i => i.Calls(stored)) == 1) return code; // Already corrected upstream.
            if (reads.Length != 1) throw new InvalidOperationException("Unrecognized OverridePacket.FromSignal implementation");
            // SetAspectOverride stores the request and fires OverrideChanged
            // BEFORE UpdateAspect evaluates it. DV Signals 1.1.3's factory
            // reads the old DISPLAYED aspect here (often -1), not the new
            // stored request. Preserve native indices and packet layout.
            reads[0].operand = stored;
            return code;
        }

        private static IEnumerable<CodeInstruction> ReplayControlState(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var modeDefault = AccessTools.PropertyGetter(typeof(DVSignal), nameof(DVSignal.IsDefaultOperationState));
            var overrideDefault = AccessTools.PropertyGetter(typeof(DVSignal), nameof(DVSignal.IsDefaultAspectOverride));
            var shuntingDefault = AccessTools.PropertyGetter(typeof(DVSignal), nameof(DVSignal.IsDefaultShuntingAllowed));
            if (code.Count(i => i.Calls(modeDefault)) != 1 || code.Count(i => i.Calls(overrideDefault)) != 1 || code.Count(i => i.Calls(shuntingDefault)) != 1)
                throw new InvalidOperationException("Unrecognized ServerManager.PlayerReady implementation");
            // 1.1.3's override-default predicate is inverted. Moreover, omitting
            // Automatic/zero leaves a reconnecting client in an old manual mode.
            // A join snapshot must send control fields, including defaults, through
            // the existing reliable native packets. No polling or extra live
            // event broadcasts. Also reset ShuntingAllowed on reconnect, as it
            // affects the native evaluator. Reservation packets are untouched.
            foreach (var read in code.Where(i => i.Calls(modeDefault) || i.Calls(overrideDefault) || i.Calls(shuntingDefault)))
            {
                read.opcode = System.Reflection.Emit.OpCodes.Call;
                read.operand = AccessTools.Method(typeof(Adapter), nameof(SkipControlSnapshot));
            }
            return code;
        }

        private static bool SkipControlSnapshot(DVSignal signal) => false;

        private void RemoveSignalSynchronization()
        {
            synchronizationHarmony?.UnpatchAll(SynchronizationPatchId);
            synchronizationHarmony = null;
            synchronizationAssembly = null;
            nextSynchronizationCheck = 0;
        }
    }
}
