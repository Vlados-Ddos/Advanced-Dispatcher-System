using System;
using HarmonyLib;

namespace AdvancedDispatcherSystem.Game
{
    public sealed partial class Dispatcher
    {
        private static System.Reflection.FieldInfo modRailwayRoot;
        // Optional completion hook, no DoubleTrack assembly reference. Track
        // loading is synchronous and replaces/splits existing RailTrack objects;
        // sector collider activation is NOT a topology change.
        private void HookTopologySources()
        {
            var type = AccessTools.TypeByName("DoubleTrack.TrackPlacer");
            if (type == null) return;
            try
            {
                var method = AccessTools.Method(type, "LoadTracks");
                if (method == null) throw new MissingMethodException(type.FullName, "LoadTracks");
                modRailwayRoot = AccessTools.Field(AccessTools.TypeByName("DoubleTrack.AllTracksPatch"), "railwayGo");
                if (modRailwayRoot == null) throw new MissingFieldException("DoubleTrack.AllTracksPatch", "railwayGo");
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(Dispatcher), nameof(BeforeModTracks)),
                    postfix: new HarmonyMethod(typeof(Dispatcher), nameof(AfterModTracks)));
            }
            catch (Exception e) { Main.Log("TOPOLOGY_SOURCE_HOOK_FAILED", e); }
        }
        private static void BeforeModTracks(ref bool __state)
        {
            // LoadTracks also receives every unrelated sceneLoaded notification.
            // Its own guard permits work only before railwayGo is assigned.
            __state = (modRailwayRoot?.GetValue(null) as UnityEngine.Object) == null;
        }
        private static void AfterModTracks(bool __state)
        {
            if (!__state) return;
            if ((modRailwayRoot?.GetValue(null) as UnityEngine.Object) != null)
                Main.Runtime?.RequestRebuild(0);
        }
    }
}
