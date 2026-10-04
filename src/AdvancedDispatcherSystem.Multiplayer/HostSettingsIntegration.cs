using AdvancedDispatcherSystem.Core;
using AdvancedDispatcherSystem.Game;
using MPAPI;
using MPAPI.Interfaces;

namespace AdvancedDispatcherSystem.Multiplayer
{
    public sealed partial class Adapter
    {
        private readonly HostSettingsSync settingsSync = new HostSettingsSync(e => Main.Log("HOST_SETTINGS_SYNC_FAILED", e));
        private void InitializeHostSettings()
        {
            MultiplayerAPI.ServerStarted += SettingsServerStarted; MultiplayerAPI.ClientStarted += SettingsClientStarted;
            MultiplayerAPI.ServerStopped += RefreshHostSettings; MultiplayerAPI.ClientStopped += RefreshHostSettings;
        }
        private void SettingsServerStarted(IServer server) => RefreshHostSettings();
        private void SettingsClientStarted(IClient client) => RefreshHostSettings();
        private void RefreshHostSettings() => settingsSync.Bind(MultiplayerAPI.Instance, MultiplayerAPI.Server, MultiplayerAPI.Client);
        public HostSettingsState CurrentHostSettings { get { RefreshHostSettings(); return settingsSync.CurrentHostSettings; } }
        public void UpdateHostSettings(HostSettingsState settings) { RefreshHostSettings(); settingsSync.UpdateHostSettings(settings); }
        private void DisposeHostSettings()
        {
            MultiplayerAPI.ServerStarted -= SettingsServerStarted; MultiplayerAPI.ClientStarted -= SettingsClientStarted;
            MultiplayerAPI.ServerStopped -= RefreshHostSettings; MultiplayerAPI.ClientStopped -= RefreshHostSettings;
            settingsSync.Dispose();
        }
    }
}
