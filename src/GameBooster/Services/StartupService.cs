using GameBooster.Models;
using Microsoft.Win32;

namespace GameBooster.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public List<StartupItem> List()
    {
        var items = new List<StartupItem>();
        ReadRun("HKCU", "Registre (utilisateur)", items);
        ReadRun("HKLM", "Registre (machine)", items);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU", "Dossier Démarrage", items);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM", "Dossier Démarrage commun", items);
        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string SetEnabled(StartupItem item, bool enabled, MachineBackup backup)
    {
        RegistryJournal.Remember(backup, item.Hive, item.ApprovedKey, item.Name);
        var current = ReadBytes(item.Hive, item.ApprovedKey, item.Name) ?? new byte[12];
        if (current.Length < 12)
        {
            var grown = new byte[12];
            Buffer.BlockCopy(current, 0, grown, 0, current.Length);
            current = grown;
        }
        current[0] = enabled ? (byte)0x02 : (byte)0x03;
        RegistryJournal.SetBinary(item.Hive, item.ApprovedKey, item.Name, current);
        return (enabled ? "Activé : " : "Désactivé : ") + item.Name + ".";
    }

    private static void ReadRun(string hive, string location, List<StartupItem> items)
    {
        using var key = Open(hive, RunKey);
        if (key == null)
            return;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var command = key.GetValue(name)?.ToString() ?? "";
            items.Add(new StartupItem
            {
                Name = name,
                Command = command,
                Location = location,
                Enabled = IsEnabled(hive, ApprovedRun, name),
                Hive = hive,
                ApprovedKey = ApprovedRun
            });
        }
    }

    private static void ReadFolder(string? folder, string hive, string location, List<StartupItem> items)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            items.Add(new StartupItem
            {
                Name = name,
                Command = file,
                Location = location,
                Enabled = IsEnabled(hive, ApprovedFolder, name),
                Hive = hive,
                ApprovedKey = ApprovedFolder
            });
        }
    }

    private static bool IsEnabled(string hive, string key, string name)
    {
        var bytes = ReadBytes(hive, key, name);
        if (bytes == null || bytes.Length == 0)
            return true;
        return bytes[0] != 0x03;
    }

    private static byte[]? ReadBytes(string hive, string key, string name)
    {
        using var opened = Open(hive, key);
        return opened?.GetValue(name) as byte[];
    }

    private static RegistryKey? Open(string hive, string path)
    {
        var root = hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        return root.OpenSubKey(path);
    }
}
