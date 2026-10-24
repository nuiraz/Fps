using System.Text.Json;
using GameBooster.Models;

namespace GameBooster.Services;

public static class PresetCatalog
{
    public static IReadOnlyList<Preset> BuiltIns { get; } = [Competitive(), Balanced(), Streaming(), Eco()];

    public static Preset? Find(string? id)
        => BuiltIns.FirstOrDefault(p => p.Id == id);

    public static string? DetectFortniteExe()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("InstallationList", out var list))
                return null;
            foreach (var item in list.EnumerateArray())
            {
                var artifact = item.TryGetProperty("ArtifactId", out var artifactId) ? artifactId.GetString() : null;
                if (artifact == null || artifact.IndexOf("Fortnite", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var location = item.TryGetProperty("InstallLocation", out var install) ? install.GetString() : null;
                if (string.IsNullOrWhiteSpace(location))
                    continue;
                var exe = Path.Combine(location, "FortniteGame", "Binaries", "Win64", "FortniteClient-Win64-Shipping.exe");
                if (File.Exists(exe))
                    return exe;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static Preset Competitive() => Make(
        "builtin-competitive",
        "competitive",
        "Compétitif / Fortnite",
        "Plan ultime, mode jeu, HAGS, captures coupées, priorité haute et réseau.",
        new TweakSelection
        {
            PowerPlan = PowerPlanChoice.Ultimate,
            GameMode = ToggleAction.Enable,
            Hags = ToggleAction.Enable,
            GameDvr = ToggleAction.Disable,
            FullscreenOptimizations = ToggleAction.Disable,
            GamePriority = ToggleAction.Enable,
            Nagle = ToggleAction.Disable,
            VisualEffects = ToggleAction.Disable,
            CleanTemp = true,
            FlushDns = true,
            CloseBackgroundApps = true,
            CreateRestorePoint = true
        });

    private static Preset Balanced() => Make(
        "builtin-balanced",
        "balanced",
        "Équilibré",
        "Performances élevées, mode jeu et nettoyage léger, sans toucher au réseau.",
        new TweakSelection
        {
            PowerPlan = PowerPlanChoice.High,
            GameMode = ToggleAction.Enable,
            GameDvr = ToggleAction.Disable,
            GamePriority = ToggleAction.Enable,
            CleanTemp = true,
            FlushDns = true,
            CreateRestorePoint = true
        });

    private static Preset Streaming() => Make(
        "builtin-streaming",
        "streaming",
        "Streaming",
        "Garde les captures et les applications ouvertes pour diffuser.",
        new TweakSelection
        {
            PowerPlan = PowerPlanChoice.High,
            GameMode = ToggleAction.Enable,
            GameDvr = ToggleAction.Enable,
            GamePriority = ToggleAction.Enable,
            CleanTemp = true,
            FlushDns = true,
            CreateRestorePoint = true
        });

    private static Preset Eco() => Make(
        "builtin-eco",
        "eco",
        "Économie",
        "Revient vers un usage quotidien sans effacer la sauvegarde d'origine de ce PC.",
        new TweakSelection
        {
            PowerPlan = PowerPlanChoice.Balanced,
            GameMode = ToggleAction.Disable,
            Hags = ToggleAction.Disable,
            GameDvr = ToggleAction.Enable,
            FullscreenOptimizations = ToggleAction.Enable,
            GamePriority = ToggleAction.Disable,
            Nagle = ToggleAction.Enable,
            VisualEffects = ToggleAction.Enable,
            CreateRestorePoint = true
        });

    private static Preset Make(string id, string key, string name, string description, TweakSelection tweaks)
    {
        var stamp = "2026-01-01T00:00:00.000Z";
        return new Preset
        {
            Id = id,
            BuiltIn = true,
            BuiltInKey = key,
            Name = name,
            Description = description,
            Tweaks = tweaks,
            CreatedAt = stamp,
            UpdatedAt = stamp
        };
    }
}
