namespace GameBooster.Models;

public sealed class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool BuiltIn { get; set; }
    public string? BuiltInKey { get; set; }
    public TweakSelection Tweaks { get; set; } = new();
    public string CreatedAt { get; set; } = "1970-01-01T00:00:00.000Z";
    public string UpdatedAt { get; set; } = "1970-01-01T00:00:00.000Z";
    public bool Deleted { get; set; }

    public Preset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        BuiltIn = BuiltIn,
        BuiltInKey = BuiltInKey,
        Tweaks = Tweaks.Clone(),
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Deleted = Deleted
    };
}

public sealed class UserPreferences
{
    public string? ActivePresetId { get; set; }
    public List<string> WatchedGames { get; set; } = [];
    public List<string> FullscreenOptimizationPaths { get; set; } = [];
    public List<string> PrecheckBackgroundApps { get; set; } = [];
    public string PingHost { get; set; } = "1.1.1.1";
    public int PingCount { get; set; } = 4;

    public static UserPreferences CreateDefault() => new()
    {
        ActivePresetId = "builtin-competitive",
        WatchedGames =
        [
            "FortniteClient-Win64-Shipping",
            "cs2",
            "r5apex",
            "VALORANT-Win64-Shipping",
            "RocketLeague"
        ],
        PingHost = "1.1.1.1",
        PingCount = 4,
        PrecheckBackgroundApps = ["discord", "spotify", "epicwebhelper", "steamwebhelper"]
    };

    public UserPreferences Clone() => new()
    {
        ActivePresetId = ActivePresetId,
        WatchedGames = [.. WatchedGames],
        FullscreenOptimizationPaths = [.. FullscreenOptimizationPaths],
        PrecheckBackgroundApps = [.. PrecheckBackgroundApps],
        PingHost = PingHost,
        PingCount = PingCount
    };
}

public sealed class SettingsDocument
{
    public string UpdatedAt { get; set; } = "1970-01-01T00:00:00.000Z";
    public UserPreferences Preferences { get; set; } = UserPreferences.CreateDefault();

    public static SettingsDocument CreateDefault() => new()
    {
        UpdatedAt = "1970-01-01T00:00:00.000Z",
        Preferences = UserPreferences.CreateDefault()
    };
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Success { get; set; }
    public string OccurredAt { get; set; } = "";
    public bool Synced { get; set; }
}

public sealed class ProfileCache
{
    public List<Preset> Presets { get; set; } = [];
    public SettingsDocument Settings { get; set; } = SettingsDocument.CreateDefault();
    public List<HistoryEntry> History { get; set; } = [];
    public string? LastSyncAt { get; set; }
}

public sealed class MachineSettings
{
    public string ApiBaseUrl { get; set; } = "https://fps-api.<subdomain>.workers.dev";
    public bool RememberSession { get; set; }
}

public sealed class SessionData
{
    public string Token { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

public sealed class RegistryValueSnapshot
{
    public string Hive { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Missing";
    public string? Data { get; set; }
}

public sealed class MachineBackup
{
    public List<RegistryValueSnapshot> Values { get; set; } = [];
    public string? PreviousPowerScheme { get; set; }
    public bool? PriorityWatcherBefore { get; set; }
    public bool PriorityWatcherEnabled { get; set; }

    public bool HasAnything =>
        Values.Count > 0 || PreviousPowerScheme != null || PriorityWatcherBefore != null;
}

public sealed class CloseableAppItem : ObservableObject
{
    private bool _isRunning;
    private bool _isSelected;

    public string ProcessName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class StartupItem
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Location { get; init; } = "";
    public bool Enabled { get; init; }
    public string Hive { get; init; } = "";
    public string ApprovedKey { get; init; } = "";
    public string ToggleLabel => Enabled ? "Désactiver" : "Activer";
}
