using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameBooster.Models;

public enum ToggleAction
{
    Leave = 0,
    Enable = 1,
    Disable = 2
}

public enum PowerPlanChoice
{
    Leave = 0,
    Ultimate = 1,
    High = 2,
    Balanced = 3
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TweakSelection : ObservableObject
{
    private PowerPlanChoice _powerPlan;
    private ToggleAction _gameMode;
    private ToggleAction _hags;
    private ToggleAction _gameDvr;
    private ToggleAction _fullscreenOptimizations;
    private ToggleAction _gamePriority;
    private ToggleAction _nagle;
    private ToggleAction _visualEffects;
    private bool _cleanTemp;
    private bool _cleanShaderCache;
    private bool _emptyRecycleBin;
    private bool _flushDns;
    private bool _closeBackgroundApps;
    private bool _createRestorePoint = true;

    public PowerPlanChoice PowerPlan { get => _powerPlan; set => Set(ref _powerPlan, value); }
    public ToggleAction GameMode { get => _gameMode; set => Set(ref _gameMode, value); }
    public ToggleAction Hags { get => _hags; set => Set(ref _hags, value); }
    public ToggleAction GameDvr { get => _gameDvr; set => Set(ref _gameDvr, value); }
    public ToggleAction FullscreenOptimizations { get => _fullscreenOptimizations; set => Set(ref _fullscreenOptimizations, value); }
    public ToggleAction GamePriority { get => _gamePriority; set => Set(ref _gamePriority, value); }
    public ToggleAction Nagle { get => _nagle; set => Set(ref _nagle, value); }
    public ToggleAction VisualEffects { get => _visualEffects; set => Set(ref _visualEffects, value); }
    public bool CleanTemp { get => _cleanTemp; set => Set(ref _cleanTemp, value); }
    public bool CleanShaderCache { get => _cleanShaderCache; set => Set(ref _cleanShaderCache, value); }
    public bool EmptyRecycleBin { get => _emptyRecycleBin; set => Set(ref _emptyRecycleBin, value); }
    public bool FlushDns { get => _flushDns; set => Set(ref _flushDns, value); }
    public bool CloseBackgroundApps { get => _closeBackgroundApps; set => Set(ref _closeBackgroundApps, value); }
    public bool CreateRestorePoint { get => _createRestorePoint; set => Set(ref _createRestorePoint, value); }

    public TweakSelection Clone() => new()
    {
        PowerPlan = PowerPlan,
        GameMode = GameMode,
        Hags = Hags,
        GameDvr = GameDvr,
        FullscreenOptimizations = FullscreenOptimizations,
        GamePriority = GamePriority,
        Nagle = Nagle,
        VisualEffects = VisualEffects,
        CleanTemp = CleanTemp,
        CleanShaderCache = CleanShaderCache,
        EmptyRecycleBin = EmptyRecycleBin,
        FlushDns = FlushDns,
        CloseBackgroundApps = CloseBackgroundApps,
        CreateRestorePoint = CreateRestorePoint
    };
}

public sealed class PowerChoice
{
    public PowerPlanChoice Value { get; init; }
    public string Label { get; init; } = "";
}

public sealed class ToggleChoice
{
    public ToggleAction Value { get; init; }
    public string Label { get; init; } = "";
}

public static class ChoiceLists
{
    public static IReadOnlyList<PowerChoice> PowerPlans { get; } =
    [
        new() { Value = PowerPlanChoice.Leave, Label = "Ne pas modifier" },
        new() { Value = PowerPlanChoice.Ultimate, Label = "Performances ultimes" },
        new() { Value = PowerPlanChoice.High, Label = "Performances élevées" },
        new() { Value = PowerPlanChoice.Balanced, Label = "Utilisation normale" }
    ];

    public static IReadOnlyList<ToggleChoice> Toggles { get; } =
    [
        new() { Value = ToggleAction.Leave, Label = "Ne pas modifier" },
        new() { Value = ToggleAction.Enable, Label = "Activer" },
        new() { Value = ToggleAction.Disable, Label = "Désactiver" }
    ];
}

public static class TweakText
{
    public static string Describe(TweakSelection tweaks)
    {
        var parts = new List<string>();
        if (tweaks.PowerPlan != PowerPlanChoice.Leave)
            parts.Add(ChoiceLists.PowerPlans.First(p => p.Value == tweaks.PowerPlan).Label);
        Add(parts, "Mode jeu", tweaks.GameMode);
        Add(parts, "HAGS", tweaks.Hags);
        Add(parts, "Captures", tweaks.GameDvr);
        Add(parts, "Plein écran", tweaks.FullscreenOptimizations);
        Add(parts, "Priorité haute", tweaks.GamePriority);
        Add(parts, "Nagle", tweaks.Nagle);
        Add(parts, "Effets visuels", tweaks.VisualEffects);
        if (tweaks.CleanTemp) parts.Add("Temp");
        if (tweaks.CleanShaderCache) parts.Add("Shaders");
        if (tweaks.EmptyRecycleBin) parts.Add("Corbeille");
        if (tweaks.FlushDns) parts.Add("DNS");
        if (tweaks.CloseBackgroundApps) parts.Add("Apps en arrière-plan");
        if (tweaks.CreateRestorePoint) parts.Add("Point de restauration");
        return parts.Count == 0 ? "Aucun réglage." : string.Join(" · ", parts);
    }

    private static void Add(List<string> parts, string label, ToggleAction action)
    {
        if (action == ToggleAction.Enable) parts.Add(label + " on");
        else if (action == ToggleAction.Disable) parts.Add(label + " off");
    }
}
