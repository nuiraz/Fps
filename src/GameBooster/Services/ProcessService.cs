using System.Diagnostics;
using GameBooster.Models;

namespace GameBooster.Services;

public static class AppCatalog
{
    public static IReadOnlyList<CloseableAppItem> CreateAppList() =>
    [
        Item("discord", "Discord"),
        Item("spotify", "Spotify"),
        Item("chrome", "Google Chrome"),
        Item("msedge", "Microsoft Edge"),
        Item("firefox", "Firefox"),
        Item("opera", "Opera"),
        Item("brave", "Brave"),
        Item("onedrive", "OneDrive"),
        Item("teams", "Microsoft Teams"),
        Item("ms-teams", "Teams (nouveau)"),
        Item("slack", "Slack"),
        Item("skype", "Skype"),
        Item("zoom", "Zoom"),
        Item("telegram", "Telegram"),
        Item("whatsapp", "WhatsApp"),
        Item("signal", "Signal"),
        Item("wallpaper64", "Wallpaper Engine"),
        Item("sharex", "ShareX"),
        Item("qbittorrent", "qBittorrent"),
        Item("utorrent", "uTorrent"),
        Item("epicwebhelper", "Epic Web Helper"),
        Item("steamwebhelper", "Steam Web Helper")
    ];

    public static readonly HashSet<string> Whitelist = new(CreateAppList().Select(a => a.ProcessName), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Denied =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "registry", "csrss", "lsass", "services", "svchost", "wininit", "winlogon",
            "smss", "explorer", "dwm", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "runtimebroker",
            "searchhost", "startmenuexperiencehost", "shellexperiencehost", "securityhealthservice",
            "msmpeng", "gamebooster", "steam", "epicgameslauncher", "fortniteclient-win64-shipping",
            "easyanticheat", "easyanticheat_eos", "beservice", "bedaisy", "vgc", "vgtray", "faceit",
            "faceitclient", "faceitservice"
        };

    public static bool IsDenied(Process process)
    {
        var name = process.ProcessName;
        if (process.Id == Environment.ProcessId)
            return true;
        if (Denied.Contains(name))
            return true;
        if (name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase)
            || name.Contains("faceit", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            if (process.SessionId == 0)
                return true;
        }
        catch
        {
            return true;
        }
        return false;
    }

    public static bool TryGetSafePath(Process process, out string path)
    {
        path = "";
        try
        {
            path = process.MainModule?.FileName ?? "";
        }
        catch
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Path.Combine(windows, "System32");
        var syswow = Path.Combine(windows, "SysWOW64");
        if (path.StartsWith(system32, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(syswow, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static CloseableAppItem Item(string process, string display) => new()
    {
        ProcessName = process,
        DisplayName = display
    };
}

public sealed class ProcessService
{
    public void RefreshRunning(IEnumerable<CloseableAppItem> apps)
    {
        HashSet<string> running;
        try
        {
            running = Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName; }
                    finally { p.Dispose(); }
                })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            running = [];
        }
        foreach (var app in apps)
            app.IsRunning = running.Contains(app.ProcessName);
    }

    public List<string> CloseSelected(IEnumerable<string> processNames)
    {
        var lines = new List<string>();
        var wanted = processNames.Where(AppCatalog.Whitelist.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            lines.Add("Aucune application autorisée n'est cochée.");
            return lines;
        }

        foreach (var name in wanted)
        {
            Process[] found;
            try { found = Process.GetProcessesByName(name); }
            catch (Exception ex)
            {
                lines.Add(name + " : " + ex.Message);
                continue;
            }
            if (found.Length == 0)
            {
                lines.Add(name + " n'est pas en cours d'exécution.");
                continue;
            }
            foreach (var process in found)
            {
                try
                {
                    if (AppCatalog.IsDenied(process) || !AppCatalog.Whitelist.Contains(process.ProcessName))
                    {
                        lines.Add(process.ProcessName + " ignoré (processus protégé).");
                        continue;
                    }
                    if (!AppCatalog.TryGetSafePath(process, out _))
                    {
                        lines.Add(process.ProcessName + " ignoré : chemin introuvable ou système.");
                        continue;
                    }
                    var closed = false;
                    try { closed = process.CloseMainWindow(); } catch { /* pas de fenêtre */ }
                    if (closed && process.WaitForExit(2000))
                    {
                        lines.Add(process.ProcessName + " fermé.");
                        continue;
                    }
                    process.Kill(entireProcessTree: false);
                    lines.Add(process.ProcessName + " arrêté.");
                }
                catch (Exception ex)
                {
                    lines.Add(process.ProcessName + " : accès refusé ou déjà fermé (" + ex.GetType().Name + ").");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return lines;
    }
}
