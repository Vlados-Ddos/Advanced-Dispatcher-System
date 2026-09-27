using System;
using AdvancedDispatcherSystem.Game;
using HarmonyLib;
using global::Multiplayer.Networking.Managers.Server;
using global::Multiplayer.Networking.TransportLayers;

namespace AdvancedDispatcherSystem.Multiplayer
{
    public sealed partial class Adapter
    {
        private Harmony jobEventsHarmony;
        private static Adapter jobEventsAdapter;
        private void HookJobEvents()
        {
            var patch = new Harmony("denis.ads.multiplayer-job-events");
            try {
                foreach (var name in new[] { "OnServerboundJobValidateRequestPacket", "OnServerboundTrainDeleteRequestPacket", "OnCommonItemChangePacket" }) {
                    var method = AccessTools.DeclaredMethod(typeof(NetworkServer), name);
                    if (method == null) throw new MissingMethodException(name);
                    patch.Patch(method,
                        prefix: new HarmonyMethod(typeof(Adapter), nameof(BeginJobPacket)) { priority = Priority.First },
                        finalizer: new HarmonyMethod(typeof(Adapter), nameof(EndJobPacket)));
                }
                jobEventsHarmony = patch; jobEventsAdapter = this;
            } catch (Exception e) {
                patch.UnpatchAll("denis.ads.multiplayer-job-events");
                UnityEngine.Debug.LogError("ADS JOB_ACTOR_UNAVAILABLE " + e);
            }
        }
        private static void BeginJobPacket(NetworkServer __instance, ITransportPeer peer, out IDisposable __state)
        {
            string id = null, name = null;
            try {
                if (jobEventsAdapter != null && __instance.TryGetServerPlayer(peer, out var player)) {
                    var wrapper = __instance.GetWrapper(player);
                    id = jobEventsAdapter.PlayerIdentity(wrapper);
                    name = wrapper.DisplayName;
                }
            } catch (Exception e) { UnityEngine.Debug.LogError("ADS JOB_ACTOR_CAPTURE_FAILED " + e); }
            __state = new JobActionContext(id, name);
        }
        private static void EndJobPacket(IDisposable __state) => __state?.Dispose();
    }
}
