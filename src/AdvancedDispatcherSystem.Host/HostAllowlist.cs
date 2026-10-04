using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

/// <summary>Explicitly normalizes local and configured remote HTTP host names.</summary>
public static class HostAllowlist
{
    public static HashSet<string> Build(IEnumerable<string> localHosts, string configured)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (localHosts != null) foreach (var value in localHosts) Add(result, value);
        if (!string.IsNullOrWhiteSpace(configured))
            foreach (var value in configured.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) Add(result, value);
        return result;
    }

    private static void Add(HashSet<string> result, string value)
    {
        if (TryNormalize(value, out var host)) result.Add(host);
    }

    public static bool TryNormalize(string value, out string host)
        => ConnectionSettingsRules.TryHost(value, out host);

    public static string[] PublicUrls(string configured, bool https, int port)
        => Build(Array.Empty<string>(), configured)
            .Select(host => new UriBuilder(https ? "https" : "http", host, port).Uri.GetLeftPart(UriPartial.Authority))
            .ToArray();
}
