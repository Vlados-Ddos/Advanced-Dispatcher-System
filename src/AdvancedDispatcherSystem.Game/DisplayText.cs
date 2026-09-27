using System;

namespace AdvancedDispatcherSystem.Game
{
    public static class DisplayText
    {
        private static readonly System.Collections.Generic.Dictionary<string,string[]> searchNames = new System.Collections.Generic.Dictionary<string,string[]>();
        public static string[] SearchNames(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return Usable(fallback) ? new[] { fallback } : Array.Empty<string>();
            string cacheKey = fallback == null ? key : key + "\n" + fallback;
            if (searchNames.TryGetValue(cacheKey, out var cached)) return cached;
            var names = new System.Collections.Generic.List<string>();
            if (Usable(fallback)) names.Add(fallback);
            foreach (var language in new[] { "English", "Russian" }) {
                var value = I2.Loc.LocalizationManager.GetTranslation(key, overrideLanguage: language);
                if (Usable(value) && value != key && !names.Contains(value)) names.Add(value);
            }
            return searchNames[cacheKey] = names.ToArray();
        }
        internal static void ResetSearchNames() => searchNames.Clear();
        public static bool Usable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim().Trim('[', ']').Trim();
            return !text.Equals("null", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("undefined", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("missing", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("passjobs/", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("w3/", StringComparison.OrdinalIgnoreCase);
        }
        public static string Station(StationController station)
        {
            var info = station?.stationInfo;
            if (info == null) return null;
            var value = string.IsNullOrEmpty(info.LocalizationKey) ? null : DV.Localization.LocalizationAPI.L(info.LocalizationKey);
            return Usable(value) && value != info.LocalizationKey ? value : Usable(info.Name) ? info.Name : null;
        }
    }
}
