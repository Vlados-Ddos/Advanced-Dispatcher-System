using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Game;
using MPAPI.Interfaces;

namespace AdvancedDispatcherSystem.Multiplayer
{
    internal sealed class HostSettingsSync : IHostSettingsProvider, IDisposable
    {
        private sealed class Subscriber { internal IPlayer player; internal Guid request; internal long requestedAt; }
        private readonly object sync = new object();
        private readonly Action<Exception> failure;
        private readonly Dictionary<byte, Subscriber> subscribers = new Dictionary<byte, Subscriber>();
        private IMultiplayerAPI api;
        private IServer server;
        private IClient client;
        private Action<IPlayer> ready, disconnected;
        private bool authority, disposed;
        private int generation;
        private Guid request;
        private long nextRequest, revision, receivedRevision;
        private HostSettingsState local, received;
        internal HostSettingsSync(Action<Exception> failure = null) { this.failure = failure; }

        internal void Bind(IMultiplayerAPI nextApi, IServer nextServer, IClient nextClient)
        {
            lock (sync)
            {
                if (disposed) return;
                bool host = nextApi != null && nextApi.IsHost, single = nextApi != null && nextApi.IsSinglePlayer;
                nextServer = host && !single ? nextServer : null; nextClient = nextApi != null && !host && !single ? nextClient : null;
                if (ReferenceEquals(api, nextApi) && ReferenceEquals(server, nextServer) && ReferenceEquals(client, nextClient) && authority == (host || single)) return;
                Detach(); generation++;
                api = nextApi; server = nextServer; client = nextClient; authority = host || single;
                request = Guid.NewGuid(); nextRequest = 0; revision = 0; receivedRevision = 0; local = received = null;
                int binding = generation;
                if (server != null)
                {
                    server.RegisterSerializablePacket<HostSettingsRequest>((packet, player) => ReceiveRequest(binding, packet, player));
                    ready = player => { lock (sync) { if (!disposed && binding == generation && player != null && subscribers.TryGetValue(player.PlayerId, out var subscriber)) Send(subscriber); } };
                    disconnected = player => { lock (sync) { if (!disposed && binding == generation && player != null) subscribers.Remove(player.PlayerId); } };
                    server.OnPlayerReady += ready; server.OnPlayerDisconnected += disconnected;
                }
                if (client != null) client.RegisterSerializablePacket<HostSettingsPacket>(packet => ReceiveSnapshot(binding, packet));
                if (api != null) api.OnTick += Tick;
            }
        }
        public HostSettingsState CurrentHostSettings
        {
            get { lock (sync) return disposed ? null : HostSettingsPacket.Copy(authority ? local : client?.IsConnected == true ? received : null); }
        }
        public void UpdateHostSettings(HostSettingsState settings)
        {
            lock (sync)
            {
                if (disposed || !authority || !HostSettingsPacket.Valid(settings) || HostSettingsPacket.Same(local, settings)) return;
                local = HostSettingsPacket.Copy(settings);
                // MPAPI's reliable flag means ReliableUnordered. The monotonic
                // process clock also keeps revisions increasing after mod reload.
                revision = Math.Max(revision + 1, Stopwatch.GetTimestamp());
                foreach (var subscriber in subscribers.Values.ToArray()) Send(subscriber);
            }
        }
        private void ReceiveRequest(int binding, HostSettingsRequest packet, IPlayer player)
        {
            lock (sync)
            {
                if (disposed || binding != generation || !authority || server == null || packet == null || packet.request == Guid.Empty || player == null || player.IsHost || server.GetPlayer(player.PlayerId) == null) return;
                long now = Stopwatch.GetTimestamp();
                if (subscribers.TryGetValue(player.PlayerId, out var previous) && now - previous.requestedAt < Stopwatch.Frequency) return;
                var subscriber = new Subscriber { player = player, request = packet.request, requestedAt = now };
                subscribers[player.PlayerId] = subscriber; Send(subscriber);
            }
        }
        private void Send(Subscriber subscriber)
        {
            if (server == null || local == null) return;
            try { server.SendSerializablePacketToPlayer(new HostSettingsPacket { request = subscriber.request, revision = revision, settings = HostSettingsPacket.Copy(local) }, subscriber.player, true); }
            catch (Exception e) { failure?.Invoke(e); }
        }
        private void ReceiveSnapshot(int binding, HostSettingsPacket packet)
        {
            lock (sync)
            {
                if (disposed || binding != generation || authority || client?.IsConnected != true || packet == null || packet.request != request || packet.revision <= receivedRevision || !HostSettingsPacket.Valid(packet.settings)) return;
                received = HostSettingsPacket.Copy(packet.settings); receivedRevision = packet.revision;
            }
        }
        private void Tick(uint tick)
        {
            lock (sync)
            {
                if (disposed || client == null) return;
                if (!client.IsConnected) { received = null; receivedRevision = 0; request = Guid.Empty; nextRequest = 0; return; }
                long now = Stopwatch.GetTimestamp(); if (now < nextRequest) return;
                if (request == Guid.Empty) request = Guid.NewGuid();
                nextRequest = now + 5 * Stopwatch.Frequency;
                // Polling also covers late host mod load/reload. Only clients
                // that ask participate, preserving Host-only mod compatibility.
                try { client.SendSerializablePacketToServer(new HostSettingsRequest { request = request }, true); }
                catch (Exception e) { failure?.Invoke(e); }
            }
        }
        private void Detach()
        {
            if (api != null) api.OnTick -= Tick;
            if (server != null) { if (ready != null) server.OnPlayerReady -= ready; if (disconnected != null) server.OnPlayerDisconnected -= disconnected; }
            ready = disconnected = null; subscribers.Clear();
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return; disposed = true; Detach(); generation++; local = received = null;
                // MPAPI has no unregister operation. Retained callbacks above
                // are inert, and a replacement adapter overwrites them safely.
                api = null; server = null; client = null;
            }
        }
    }
}
