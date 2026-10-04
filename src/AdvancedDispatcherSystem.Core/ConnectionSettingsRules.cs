using System;
using System.Net;

namespace AdvancedDispatcherSystem.Core
{
    public static class ConnectionSettingsRules
    {
        public static bool TryPort(string text, out int port)
            => int.TryParse(text, out port) && port >= 1024 && port <= 65535;

        public static bool TryHost(string text, out string host)
        {
            host = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.Length > 253 || text.Contains("/") || text.Contains("\\")) return false;
            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal)) text = text.Substring(1, text.Length - 2);
            if (IPAddress.TryParse(text, out var address)) { host = address.ToString(); return true; }
            text = text.TrimEnd('.');
            if (text.Length == 0 || Uri.CheckHostName(text) != UriHostNameType.Dns) return false;
            foreach (var label in text.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label) if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
            }
            // HTTP Host headers and certificate DNS names use the ASCII IDN
            // form, even when the user enters a Cyrillic/international name.
            try { host = new System.Globalization.IdnMapping().GetAscii(text); return true; }
            catch (ArgumentException) { return false; }
        }

        public static bool ValidHosts(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            foreach (var entry in text.Split(new[] { ',', ';' }, StringSplitOptions.None))
                if (!TryHost(entry, out _)) return false;
            return true;
        }
    }
}
