using System;
using System.Collections.Generic;
using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Game;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;

namespace AdvancedDispatcherSystem.Multiplayer
{
    public sealed partial class Adapter : IMultiplayerAdapter
    {
        private readonly Dictionary<byte, string> identities = new Dictionary<byte, string>();
        private readonly Dictionary<byte, IPlayer> references = new Dictionary<byte, IPlayer>();
        private long generation;
        private string previousMode;
        private IMultiplayerAPI registeredApi;
        private readonly StandaloneAdapter standalone = new StandaloneAdapter();
        public Adapter()
        {
            if (MultiplayerAPI.LoadedApiVersion != "1.1.0.0") throw new NotSupportedException("MP_API_VERSION " + MultiplayerAPI.LoadedApiVersion);
            RegisterWhenReady();
            HookJobEvents();
        }
        private void RegisterWhenReady()
        {
            var api = MultiplayerAPI.Instance;
            if (api == null || ReferenceEquals(api, registeredApi)) return;
            api.SetModCompatibility("AdvancedDispatcherSystem", MultiplayerCompatibility.Host);
            registeredApi = api;
        }
        public string Version => MultiplayerAPI.MultiplayerVersion ?? "";
        public bool Authority => MultiplayerAPI.Instance != null && (MultiplayerAPI.Instance.IsSinglePlayer || MultiplayerAPI.Instance.IsHost);
        public string Mode { get { RegisterWhenReady(); return MultiplayerAPI.Instance == null ? "multiplayer-unavailable" : MultiplayerAPI.Instance.IsSinglePlayer ? "singleplayer" : MultiplayerAPI.Instance.IsHost ? "host" : "client"; } }
        private string PlayerIdentity(IPlayer player)
        {
            string mode = Mode;
            if (mode != previousMode) { previousMode = mode; identities.Clear(); references.Clear(); }
            if (!references.TryGetValue(player.PlayerId, out var old) || !ReferenceEquals(old, player)) {
                references[player.PlayerId] = player;
                identities[player.PlayerId] = "p" + player.PlayerId + ":" + (++generation);
            }
            return identities[player.PlayerId];
        }
        public PlayerState[] CapturePlayers()
        {
            string mode = Mode;
            if (mode != previousMode) { previousMode = mode; identities.Clear(); references.Clear(); }
            if (mode == "singleplayer") return standalone.CapturePlayers();
            IReadOnlyCollection<IPlayer> players = MultiplayerAPI.Server?.Players ?? MultiplayerAPI.Client?.Players;
            if (players == null) return new PlayerState[0];
            var live = new HashSet<byte>(); var result = new List<PlayerState>(players.Count);
            foreach (var player in players)
            {
                if (player == null) continue;
                live.Add(player.PlayerId);
                PlayerIdentity(player);
                if (!player.IsLoaded) continue;
                var p = player.Position - WorldMover.currentMove;
                result.Add(PlayerPose.Attach(new PlayerState { id = identities[player.PlayerId], name = player.DisplayName, x = p.x, z = p.z, yaw = player.RotationY, host = player.IsHost, car = player.OccupiedCar == null ? null : player.OccupiedCar.CarGUID, sampledAt = Protocol.Now }, player.OccupiedCar));
            }
            var removed = new List<byte>(); foreach (var id in identities.Keys) if (!live.Contains(id)) removed.Add(id);
            foreach (var id in removed) { references.Remove(id); identities.Remove(id); }
            return result.ToArray();
        }
        public void Dispose() { jobEventsHarmony?.UnpatchAll("denis.ads.multiplayer-job-events"); jobEventsHarmony = null; if (ReferenceEquals(jobEventsAdapter, this)) jobEventsAdapter = null; identities.Clear(); references.Clear(); protectionHarmony?.UnpatchAll("denis.ads.multiplayer-protection"); protectionHarmony=null; }
    }
}
