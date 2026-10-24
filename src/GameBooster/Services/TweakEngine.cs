using GameBooster.Models;
using Microsoft.Win32;

namespace GameBooster.Services;

public readonly record struct BoostProgress(double Percent, string Label);

public sealed class TweakEngine
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string Graphics = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string GameConfig = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string GameDvrPolicy = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string VisualFx = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Desktop = @"Control Panel\Desktop";
    private const string Metrics = @"Control Panel\Desktop\WindowMetrics";
    private const string FsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";
    private static readonly byte[] PerformanceMask = [0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00];

    private readonly AppState _state;
    private readonly LocalStore _store;
    private readonly PowerPlanService _power;
    private readonly NetworkService _network;
    private readonly CleanupService _cleanup;
    private readonly ProcessService _processes;
    private readonly RestorePointService _restorePoints;
    private readonly GamePriorityService _priority;
    private readonly AppLog _log;
    public Action? RequestSync { get; set; }

    public TweakEngine(
        AppState state,
        LocalStore store,
        PowerPlanService power,
        NetworkService network,
        CleanupService cleanup,
        ProcessService processes,
        RestorePointService restorePoints,
        GamePriorityService priority,
        AppLog log)
    {
        _state = state;
        _store = store;
        _power = power;
        _network = network;
        _cleanup = cleanup;
        _processes = processes;
        _restorePoints = restorePoints;
        _priority = priority;
        _log = log;
    }

    public Task<IReadOnlyList<string>> ApplyAsync(TweakSelection tweaks, IReadOnlyList<string> appsToClose, string? presetId, string? presetName, string action = "boost", IProgress<BoostProgress>? progress = null)
    {
        return Task.Run(() =>
        {
            var lines = new List<string>();
            var failed = false;
            var steps = new List<(string Title, Action Work)>();
            void Add(string title, Action work) => steps.Add((title, work));

            progress?.Report(new BoostProgress(2, tweaks.CreateRestorePoint ? "Point de restauration" : "Préparation"));
            if (tweaks.CreateRestorePoint)
                lines.Add(_restorePoints.Create("Game Booster"));

            Add("Plan d'alimentation", () =>
            {
                lock (_state.Gate)
                    lines.Add(_power.Apply(tweaks.PowerPlan, _state.Backup));
            });
            Add("Mode jeu", () => lines.Add(ApplyGameMode(tweaks.GameMode)));
            Add("HAGS", () => lines.Add(ApplyHags(tweaks.Hags)));
            Add("Captures", () => lines.Add(ApplyDvr(tweaks.GameDvr)));
            Add("Plein écran", () => lines.AddRange(ApplyFso(tweaks.FullscreenOptimizations)));
            Add("Priorité", () => lines.Add(ApplyPriority(tweaks.GamePriority)));
            Add("Nagle", () =>
            {
                lock (_state.Gate)
                    lines.AddRange(_network.ApplyNagle(tweaks.Nagle, _state.Backup));
            });
            Add("Effets visuels", () => lines.AddRange(ApplyVisuals(tweaks.VisualEffects)));

            if (tweaks.CloseBackgroundApps)
                Add("Applications", () => lines.AddRange(_processes.CloseSelected(appsToClose)));
            if (tweaks.CleanTemp)
                Add("Fichiers temporaires", () => lines.AddRange(_cleanup.CleanTemp().Notes));
            if (tweaks.CleanShaderCache)
            {
                lines.Add("Les caches de shaders seront reconstruits au prochain lancement des jeux.");
                Add("Shaders", () => lines.AddRange(_cleanup.CleanShaderCaches().Notes));
            }
            if (tweaks.EmptyRecycleBin)
                Add("Corbeille", () => lines.Add(_cleanup.EmptyRecycleBin()));
            if (tweaks.FlushDns)
                Add("DNS", () => lines.Add(_network.FlushDns()));

            var total = Math.Max(1, steps.Count);
            for (var i = 0; i < steps.Count; i++)
            {
                progress?.Report(new BoostProgress(i * 100d / total, steps[i].Title));
                try
                {
                    steps[i].Work();
                }
                catch (Exception ex)
                {
                    failed = true;
                    lines.Add(steps[i].Title + " : " + ex.Message);
                    _log.Error(steps[i].Title, ex);
                }
            }
            progress?.Report(new BoostProgress(100, "Terminé"));

            lock (_state.Gate)
                _store.SaveBackup(_state.Backup);

            var detail = string.Join(" ", lines);
            if (detail.Length > 3500)
                detail = detail[..3500];
            var title = presetName ?? (action == "cleanup" ? "Nettoyage" : action == "network" ? "Réseau" : "Boost");
            AddHistory(string.IsNullOrWhiteSpace(presetName) ? action : "preset", title + " — " + detail, !failed);
            if (!failed && (action == "boost" || !string.IsNullOrWhiteSpace(presetName)))
                _state.BoostApplied = true;
            if (!string.IsNullOrWhiteSpace(presetId))
                TouchActivePreset(presetId);
            return (IReadOnlyList<string>)lines;
        });
    }

    public Task<IReadOnlyList<string>> RestoreAsync()
    {
        return Task.Run(() =>
        {
            var lines = new List<string>();
            lock (_state.Gate)
            {
                if (!_state.Backup.HasAnything)
                {
                    lines.Add("Aucune modification réversible n'est enregistrée sur ce PC.");
                    return (IReadOnlyList<string>)lines;
                }
                try
                {
                    RegistryJournal.RestoreAll(_state.Backup);
                    lines.Add("Valeurs de registre d'origine réécrites.");
                    BroadcastVisuals();
                }
                catch (Exception ex)
                {
                    lines.Add("Registre : " + ex.Message);
                    _log.Error("Restauration du registre", ex);
                }
                try
                {
                    var power = _power.Restore(_state.Backup);
                    if (power != null)
                        lines.Add(power);
                }
                catch (Exception ex)
                {
                    lines.Add("Plan d'alimentation : " + ex.Message);
                }
                var priority = _priority.Restore(_state.Backup);
                if (!string.IsNullOrWhiteSpace(priority))
                    lines.Add(priority);
                _store.SaveBackup(_state.Backup);
            }
            lines.Add("Les fichiers déjà supprimés (temporaires, shaders, corbeille) ne sont pas récupérés.");
            _state.BoostApplied = false;
            AddHistory("restore", string.Join(" ", lines), true);
            return (IReadOnlyList<string>)lines;
        });
    }

    public void AddHistory(string action, string detail, bool success)
    {
        var entry = new HistoryEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Action = action,
            Detail = detail.Length > 3900 ? detail[..3900] : detail,
            Success = success,
            OccurredAt = TimeStamp.Now(),
            Synced = false
        };
        lock (_state.Gate)
        {
            _state.Profile.History.Insert(0, entry);
            if (_state.Profile.History.Count > 200)
                _state.Profile.History = _state.Profile.History.Take(200).ToList();
            _store.SaveProfile(_state.ProfileKey, _state.Profile);
        }
        _state.Notify();
        RequestSync?.Invoke();
    }

    private string ApplyGameMode(ToggleAction action)
    {
        if (action == ToggleAction.Leave)
            return "Mode jeu laissé tel quel.";
        lock (_state.Gate)
        {
            RegistryJournal.Remember(_state.Backup, "HKCU", GameBar, "AutoGameModeEnabled");
            RegistryJournal.Remember(_state.Backup, "HKCU", GameBar, "AllowAutoGameMode");
            var value = action == ToggleAction.Enable ? 1u : 0u;
            RegistryJournal.SetDword("HKCU", GameBar, "AutoGameModeEnabled", value);
            RegistryJournal.SetDword("HKCU", GameBar, "AllowAutoGameMode", value);
        }
        return action == ToggleAction.Enable ? "Mode jeu activé." : "Mode jeu désactivé.";
    }

    private string ApplyHags(ToggleAction action)
    {
        if (action == ToggleAction.Leave)
            return "HAGS laissé tel quel.";
        lock (_state.Gate)
        {
            RegistryJournal.Remember(_state.Backup, "HKLM", Graphics, "HwSchMode");
            RegistryJournal.SetDword("HKLM", Graphics, "HwSchMode", action == ToggleAction.Enable ? 2u : 1u);
        }
        return (action == ToggleAction.Enable ? "HAGS activé." : "HAGS désactivé.")
            + " Un redémarrage est nécessaire. Game Booster ne redémarre pas l'ordinateur.";
    }

    private string ApplyDvr(ToggleAction action)
    {
        if (action == ToggleAction.Leave)
            return "Captures laissées telles quelles.";
        var value = action == ToggleAction.Enable ? 1u : 0u;
        lock (_state.Gate)
        {
            RegistryJournal.Remember(_state.Backup, "HKCU", GameConfig, "GameDVR_Enabled");
            RegistryJournal.Remember(_state.Backup, "HKCU", GameDvr, "AppCaptureEnabled");
            RegistryJournal.Remember(_state.Backup, "HKLM", GameDvrPolicy, "AllowGameDVR");
            RegistryJournal.SetDword("HKCU", GameConfig, "GameDVR_Enabled", value);
            RegistryJournal.SetDword("HKCU", GameDvr, "AppCaptureEnabled", value);
            RegistryJournal.SetDword("HKLM", GameDvrPolicy, "AllowGameDVR", value);
        }
        return action == ToggleAction.Enable
            ? "Captures Xbox Game Bar activées."
            : "Captures Xbox Game Bar désactivées. La Game Bar elle-même n'est pas fermée.";
    }

    private List<string> ApplyFso(ToggleAction action)
    {
        var lines = new List<string>();
        if (action == ToggleAction.Leave)
        {
            lines.Add("Optimisations plein écran laissées telles quelles.");
            return lines;
        }
        string[] paths;
        lock (_state.Gate)
            paths = _state.Profile.Settings.Preferences.FullscreenOptimizationPaths.ToArray();
        if (paths.Length == 0)
        {
            lines.Add("Aucun exécutable choisi pour les optimisations plein écran.");
            return lines;
        }
        foreach (var raw in paths)
        {
            var path = (raw ?? "").Trim();
            if (!IsSafeExePath(path))
            {
                lines.Add("Chemin ignoré : " + path);
                continue;
            }
            lock (_state.Gate)
                RegistryJournal.Remember(_state.Backup, "HKCU", Layers, path);
            var current = ReadLayer(path);
            var next = action == ToggleAction.Disable ? AddFlag(current) : RemoveFlag(current);
            if (string.IsNullOrWhiteSpace(next))
            {
                using var key = Registry.CurrentUser.CreateSubKey(Layers, writable: true);
                if (key.GetValueNames().Any(n => n.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    key.DeleteValue(path, throwOnMissingValue: false);
            }
            else
            {
                RegistryJournal.SetString("HKCU", Layers, path, next);
            }
            lines.Add((action == ToggleAction.Disable ? "Optimisations plein écran désactivées pour " : "Optimisations plein écran activées pour ") + Path.GetFileName(path) + ".");
        }
        return lines;
    }

    private string ApplyPriority(ToggleAction action)
    {
        if (action == ToggleAction.Leave)
            return "Priorité des jeux laissée telle quelle.";
        _priority.SetEnabled(action == ToggleAction.Enable, _state.Backup);
        return action == ToggleAction.Enable
            ? "Priorité Haute demandée pour les jeux surveillés. Jamais Temps réel, aucune lecture mémoire."
            : "Surveillance de priorité arrêtée.";
    }

    private List<string> ApplyVisuals(ToggleAction action)
    {
        var lines = new List<string>();
        if (action == ToggleAction.Leave)
        {
            lines.Add("Effets visuels laissés tels quels.");
            return lines;
        }
        lock (_state.Gate)
        {
            RememberVisuals();
            if (action == ToggleAction.Disable)
            {
                RegistryJournal.SetDword("HKCU", VisualFx, "VisualFXSetting", 2);
                RegistryJournal.SetDword("HKCU", Personalize, "EnableTransparency", 0);
                RegistryJournal.SetString("HKCU", Metrics, "MinAnimate", "0");
                RegistryJournal.SetString("HKCU", Desktop, "DragFullWindows", "0");
                RegistryJournal.SetString("HKCU", Desktop, "MenuShowDelay", "0");
                RegistryJournal.SetBinary("HKCU", Desktop, "UserPreferencesMask", PerformanceMask);
                ApplySpi(false);
                lines.Add("Effets visuels réglés pour les performances.");
            }
            else
            {
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", VisualFx, "VisualFXSetting");
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", Personalize, "EnableTransparency");
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", Metrics, "MinAnimate");
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", Desktop, "DragFullWindows");
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", Desktop, "MenuShowDelay");
                RegistryJournal.WriteBackIfPresent(_state.Backup, "HKCU", Desktop, "UserPreferencesMask");
                var drag = RegistryJournal.ReadDword("HKCU", Desktop, "DragFullWindows");
                var enabled = true;
                using (var key = Registry.CurrentUser.OpenSubKey(Desktop))
                {
                    var text = key?.GetValue("DragFullWindows") as string;
                    if (text == "0")
                        enabled = false;
                }
                _ = drag;
                ApplySpi(enabled);
                lines.Add("Effets visuels ramenés aux valeurs d'origine enregistrées.");
            }
        }
        return lines;
    }

    private void RememberVisuals()
    {
        RegistryJournal.Remember(_state.Backup, "HKCU", VisualFx, "VisualFXSetting");
        RegistryJournal.Remember(_state.Backup, "HKCU", Personalize, "EnableTransparency");
        RegistryJournal.Remember(_state.Backup, "HKCU", Metrics, "MinAnimate");
        RegistryJournal.Remember(_state.Backup, "HKCU", Desktop, "DragFullWindows");
        RegistryJournal.Remember(_state.Backup, "HKCU", Desktop, "MenuShowDelay");
        RegistryJournal.Remember(_state.Backup, "HKCU", Desktop, "UserPreferencesMask");
    }

    private static void ApplySpi(bool animations)
    {
        var flag = animations ? 1u : 0u;
        NativeMethods.SystemParametersInfo(NativeMethods.SpiSetDragFullWindows, flag, IntPtr.Zero, NativeMethods.SpifUpdateAndNotify);
        NativeMethods.SystemParametersInfo(NativeMethods.SpiSetMenuAnimation, 0, new IntPtr(animations ? 1 : 0), NativeMethods.SpifUpdateAndNotify);
        NativeMethods.SystemParametersInfo(NativeMethods.SpiSetClientAreaAnimation, 0, new IntPtr(animations ? 1 : 0), NativeMethods.SpifUpdateAndNotify);
        BroadcastVisuals();
    }

    private static void BroadcastVisuals()
    {
        NativeMethods.SendMessageTimeout(NativeMethods.HwndBroadcast, NativeMethods.WmSettingChange, UIntPtr.Zero, "WindowMetrics", NativeMethods.SmtoAbortIfHung, 200, out _);
    }

    private void TouchActivePreset(string presetId)
    {
        lock (_state.Gate)
        {
            _state.Profile.Settings.Preferences.ActivePresetId = presetId;
            _state.Profile.Settings.UpdatedAt = TimeStamp.Touch(_state.Profile.Settings.UpdatedAt);
            _store.SaveProfile(_state.ProfileKey, _state.Profile);
        }
        _state.Notify();
        RequestSync?.Invoke();
    }

    private static string? ReadLayer(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Layers);
        return key?.GetValue(path, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static string AddFlag(string? current)
    {
        var parts = (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains(FsoFlag, StringComparer.OrdinalIgnoreCase))
            parts.Add(FsoFlag);
        return string.Join(' ', parts);
    }

    private static string RemoveFlag(string? current)
    {
        var parts = (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.Equals(FsoFlag, StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', parts);
    }

    private static bool IsSafeExePath(string path)
    {
        if (path.Length < 5 || path.Length > 260)
            return false;
        if (!Path.IsPathRooted(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(Path.Combine(windows, "System32"), StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith(Path.Combine(windows, "SysWOW64"), StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
