using System.Diagnostics;
using GameBooster.Models;

namespace GameBooster.Services;

public sealed class GamePriorityService : IDisposable
{
    private readonly AppState _state;
    private readonly AppLog _log;
    private readonly System.Threading.Timer _timer;
    private readonly HashSet<int> _raised = [];

    public GamePriorityService(AppState state, AppLog log)
    {
        _state = state;
        _log = log;
        _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void Tick()
    {
        bool enabled;
        string[] names;
        lock (_state.Gate)
        {
            enabled = _state.Backup.PriorityWatcherEnabled;
            names = _state.Profile.Settings.Preferences.WatchedGames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        if (!enabled || names.Length == 0)
            return;

        foreach (var name in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var process in processes)
            {
                try
                {
                    if (!CanRaise(process))
                        continue;
                    if (process.PriorityClass != ProcessPriorityClass.High)
                    {
                        process.PriorityClass = ProcessPriorityClass.High;
                        if (_raised.Add(process.Id))
                            _log.Info("Priorité Haute pour " + process.ProcessName + " (" + process.Id + ").");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Priorité non modifiée pour " + name, ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        if (_raised.Count > 200)
            _raised.Clear();
    }

    private static bool CanRaise(Process process)
    {
        if (process.Id == Environment.ProcessId)
            return false;
        try
        {
            if (process.SessionId == 0)
                return false;
        }
        catch
        {
            return false;
        }
        var name = process.ProcessName;
        if (name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("faceit", StringComparison.OrdinalIgnoreCase))
            return false;
        return name.ToLowerInvariant() switch
        {
            "system" or "idle" or "registry" or "csrss" or "lsass" or "services" or "svchost" or "wininit"
                or "winlogon" or "smss" or "explorer" or "dwm" or "gamebooster" or "msmpeng" => false,
            _ => true
        };
    }

    public void SetEnabled(bool enabled, MachineBackup backup)
    {
        lock (_state.Gate)
        {
            backup.PriorityWatcherBefore ??= backup.PriorityWatcherEnabled;
            backup.PriorityWatcherEnabled = enabled;
        }
    }

    public string Restore(MachineBackup backup)
    {
        lock (_state.Gate)
        {
            if (backup.PriorityWatcherBefore == null)
                return "";
            backup.PriorityWatcherEnabled = backup.PriorityWatcherBefore.Value;
            backup.PriorityWatcherBefore = null;
            return backup.PriorityWatcherEnabled
                ? "Surveillance de priorité rétablie."
                : "Surveillance de priorité arrêtée.";
        }
    }

    public void Dispose() => _timer.Dispose();
}
