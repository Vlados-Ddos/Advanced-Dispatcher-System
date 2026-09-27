using System;
using System.Collections;
using System.Reflection;
using UnityModManagerNet;

namespace AdvancedDispatcherSystem.Game
{
    // MPAPI has no job ownership surface. Read NetworkedJob itself; the
    // current booklet holder is not necessarily the owner of the job.
    internal static class JobOwnership
    {
        private static Assembly source;
        private static MethodInfo findJob;
        private static PropertyInfo ownedBy, instance, server, players, guid, username;
        internal static string ReadIdentity(string jobId, string mode, out string name, out string key)
        {
            name = null; key = null;
            if (mode == "singleplayer") { key = "local"; return "local"; }
            if (mode != "host" && mode != "client") return "unknown";
            try
            {
                var mod = UnityModManager.FindMod("Multiplayer");
                if (mod?.Active != true || mod.Assembly == null) return "unknown";
                if (source != mod.Assembly)
                {
                    source = mod.Assembly;
                    var job = source.GetType("Multiplayer.Components.Networking.Jobs.NetworkedJob");
                    var life = source.GetType("Multiplayer.Components.Networking.NetworkLifecycle");
                    findJob = job?.GetMethod("TryGetFromJobId", BindingFlags.Public | BindingFlags.Static);
                    ownedBy = job?.GetProperty("OwnedBy");
                    instance = life?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    server = life?.GetProperty("Server");
                    players = server?.PropertyType.GetProperty("ServerPlayers");
                    var player = source.GetType("Multiplayer.Networking.Data.ServerPlayer");
                    guid = player?.GetProperty("Guid"); username = player?.GetProperty("Username");
                }
                if (findJob == null || ownedBy == null) return "unknown";
                var args = new object[] { jobId, null };
                if (!(bool)findJob.Invoke(null, args) || args[1] == null) return "unknown";
                if (!(ownedBy.GetValue(args[1], null) is Guid owner)) return "unknown";
                if (owner == Guid.Empty) return "shared";
                key = owner.ToString("N");
                var lifecycle = instance?.GetValue(null, null);
                var host = lifecycle == null ? null : server?.GetValue(lifecycle, null);
                var members = host == null ? null : players?.GetValue(host, null) as IEnumerable;
                if (members != null && guid != null && username != null)
                    foreach (var member in members)
                        if (guid.GetValue(member, null) is Guid id && id == owner)
                        { name = username.GetValue(member, null) as string; break; }
                return "assigned";
            }
            catch (Exception e) { Main.Log("JOB_OWNER_CAPTURE_FAILED", e); return "unknown"; }
        }
    }
}
