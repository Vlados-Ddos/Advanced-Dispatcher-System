using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    public interface IHostSettingsProvider
    {
        HostSettingsState CurrentHostSettings { get; }
        void UpdateHostSettings(HostSettingsState settings);
    }
}
