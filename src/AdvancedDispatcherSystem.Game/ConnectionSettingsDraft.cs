using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Game
{
    // Text editing is kept separate from live/persisted settings. In particular,
    // deleting a port digit must not immediately put the old number back.
    internal sealed class ConnectionSettingsDraft
    {
        internal string Port, PublicHost;
        internal bool Remote;
        internal void Reset(Settings settings)
        { Port = settings.Port.ToString(); PublicHost = settings.PublicHost ?? ""; Remote = settings.RemoteLanAccess; }
        internal string Error => !ConnectionSettingsRules.TryPort(Port, out _) ? "invalidPort"
            : !ConnectionSettingsRules.ValidHosts(PublicHost) ? "invalidHost" : null;
        internal bool Changed(Settings settings) => Port != settings.Port.ToString() || (PublicHost ?? "") != (settings.PublicHost ?? "") || Remote != settings.RemoteLanAccess;
        internal bool Apply(Settings settings)
        {
            if (Error != null) return false;
            ConnectionSettingsRules.TryPort(Port, out var port);
            settings.Port = port; settings.PublicHost = (PublicHost ?? "").Trim(); settings.RemoteLanAccess = Remote;
            Reset(settings);
            return true;
        }
    }
}
