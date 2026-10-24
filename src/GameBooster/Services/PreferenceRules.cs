using System.Text.RegularExpressions;
using GameBooster.Models;

namespace GameBooster.Services;

public static partial class PreferenceRules
{
    private static readonly HashSet<string> Apps = new(StringComparer.Ordinal)
    {
        "discord", "spotify", "chrome", "msedge", "firefox", "opera", "brave", "onedrive", "teams", "ms-teams",
        "slack", "skype", "zoom", "telegram", "whatsapp", "signal", "wallpaper64", "sharex", "qbittorrent",
        "utorrent", "epicwebhelper", "steamwebhelper"
    };

    [GeneratedRegex(@"^[A-Za-z]:\\[^<>:""|?*\r\n]{1,252}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExePathPattern();

    [GeneratedRegex("^[A-Za-z0-9:._-]{1,253}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessPattern();

    public static bool IsPresetId(string? id)
        => Guid.TryParseExact(id, "D", out _) && !id!.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase);

    public static bool IsHost(string? value) => value != null && HostPattern().IsMatch(value);

    public static bool IsProcessName(string? value) => value != null && ProcessPattern().IsMatch(value);

    public static bool IsBackgroundApp(string? value)
        => value != null && Apps.Contains(value.Trim().ToLowerInvariant());

    public static string? CanonicalExe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var path = value.Trim().Replace('/', '\\');
        return ExePathPattern().IsMatch(path) ? path : null;
    }

    public static UserPreferences Sanitize(UserPreferences? source)
    {
        source ??= UserPreferences.CreateDefault();
        var active = string.IsNullOrWhiteSpace(source.ActivePresetId) ? null : source.ActivePresetId.Trim();
        if (active != null && active.Length > 64)
            active = null;
        var host = (source.PingHost ?? "").Trim();
        if (!IsHost(host))
            host = "1.1.1.1";
        var count = source.PingCount;
        if (count < 1 || count > 10)
            count = 4;
        return new UserPreferences
        {
            ActivePresetId = active,
            WatchedGames = Filter(source.WatchedGames, 30, IsProcessName, keepCase: true),
            FullscreenOptimizationPaths = Filter(source.FullscreenOptimizationPaths, 20, item => CanonicalExe(item) != null, keepCase: true)
                .Select(item => CanonicalExe(item)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList(),
            PrecheckBackgroundApps = Filter(source.PrecheckBackgroundApps, 40, IsBackgroundApp, keepCase: false),
            PingHost = host,
            PingCount = count
        };
    }

    public static bool Changed(UserPreferences? source, UserPreferences cleaned)
    {
        source ??= UserPreferences.CreateDefault();
        return !string.Equals(source.ActivePresetId ?? "", cleaned.ActivePresetId ?? "", StringComparison.Ordinal)
            || !string.Equals(source.PingHost ?? "", cleaned.PingHost, StringComparison.Ordinal)
            || source.PingCount != cleaned.PingCount
            || !Sequence(source.WatchedGames).SequenceEqual(cleaned.WatchedGames, StringComparer.Ordinal)
            || !Sequence(source.FullscreenOptimizationPaths).SequenceEqual(cleaned.FullscreenOptimizationPaths, StringComparer.OrdinalIgnoreCase)
            || !Sequence(source.PrecheckBackgroundApps).SequenceEqual(cleaned.PrecheckBackgroundApps, StringComparer.Ordinal);
    }

    private static List<string> Filter(List<string>? source, int max, Func<string, bool> accept, bool keepCase)
    {
        var items = new List<string>();
        if (source == null)
            return items;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in source)
        {
            if (items.Count >= max || string.IsNullOrWhiteSpace(raw))
                continue;
            var value = keepCase ? raw.Trim() : raw.Trim().ToLowerInvariant();
            if (!accept(value) || !seen.Add(value))
                continue;
            items.Add(value);
        }
        return items;
    }

    private static IEnumerable<string> Sequence(List<string>? values) => values ?? [];
}
