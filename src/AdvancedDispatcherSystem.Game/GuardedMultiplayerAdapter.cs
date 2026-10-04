using System;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    // Optional provider failures must revoke authority, including failures in
    // getters used before the ordinary capture try/catch.
    internal sealed class GuardedMultiplayerAdapter : IMultiplayerAdapter, IHostSettingsProvider
    {
        private readonly IMultiplayerAdapter source;
        private readonly Action<Exception> failed;
        private bool unavailable, disposed;
        public GuardedMultiplayerAdapter(IMultiplayerAdapter source, Action<Exception> failed)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.failed = failed; }
        private T Read<T>(Func<T> read, T fallback)
        {
            if (unavailable || disposed) return fallback;
            try { return read(); }
            catch (Exception e) { unavailable = true; failed(e); return fallback; }
        }
        public string Version => Read(() => source.Version, "");
        public string Mode => Read(() => source.Mode, "multiplayer-unavailable");
        public bool Authority => Read(() => source.Authority, false);
        public bool ProtectedSwitches => Read(() => source.ProtectedSwitches, false);
        public bool PublishSignalReservation(int signal, bool reserved) => Read(() => source.PublishSignalReservation(signal, reserved), false);
        public PlayerState[] CapturePlayers() => Read(() => source.CapturePlayers(), Array.Empty<PlayerState>());
        public HostSettingsState CurrentHostSettings => Read(() => (source as IHostSettingsProvider)?.CurrentHostSettings, (HostSettingsState)null);
        public void UpdateHostSettings(HostSettingsState settings) => Read(() => { (source as IHostSettingsProvider)?.UpdateHostSettings(settings); return true; }, false);
        public void Dispose() { if (disposed) return; disposed = true; source.Dispose(); }
    }
}
