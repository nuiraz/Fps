using System.Text.Json;
using GameBooster.Models;
using Microsoft.Win32;

namespace GameBooster.Services;

public static class RegistryJournal
{
    public static void Remember(MachineBackup backup, string hive, string key, string name)
    {
        if (Find(backup, hive, key, name) != null)
            return;
        backup.Values.Add(Capture(hive, key, name));
    }

    public static void SetDword(string hive, string key, string name, uint value)
    {
        using var opened = Open(hive, key, writable: true);
        opened.SetValue(name, unchecked((int)value), RegistryValueKind.DWord);
    }

    public static void SetString(string hive, string key, string name, string value, bool expand = false)
    {
        using var opened = Open(hive, key, writable: true);
        opened.SetValue(name, value, expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public static void SetBinary(string hive, string key, string name, byte[] value)
    {
        using var opened = Open(hive, key, writable: true);
        opened.SetValue(name, value, RegistryValueKind.Binary);
    }

    public static void WriteBackIfPresent(MachineBackup backup, string hive, string key, string name)
    {
        var snap = Find(backup, hive, key, name);
        if (snap != null)
            Apply(snap);
    }

    public static void RestoreAll(MachineBackup backup)
    {
        foreach (var snap in backup.Values)
            Apply(snap);
        backup.Values.Clear();
    }

    public static uint? ReadDword(string hive, string key, string name)
    {
        using var opened = TryOpen(hive, key);
        if (opened == null)
            return null;
        var value = opened.GetValue(name);
        return value is int number ? unchecked((uint)number) : null;
    }

    private static RegistryValueSnapshot? Find(MachineBackup backup, string hive, string key, string name)
        => backup.Values.FirstOrDefault(v =>
            v.Hive.Equals(hive, StringComparison.OrdinalIgnoreCase)
            && v.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
            && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static RegistryValueSnapshot Capture(string hive, string key, string name)
    {
        var snap = new RegistryValueSnapshot { Hive = hive, Key = key, Name = name, Kind = "Missing" };
        using var opened = TryOpen(hive, key);
        if (opened == null || !opened.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return snap;
        var kind = opened.GetValueKind(name);
        var raw = opened.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        switch (kind)
        {
            case RegistryValueKind.DWord:
                snap.Kind = "DWord";
                snap.Data = raw is int dword ? unchecked((uint)dword).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
                break;
            case RegistryValueKind.QWord:
                snap.Kind = "QWord";
                snap.Data = raw is long qword ? unchecked((ulong)qword).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
                break;
            case RegistryValueKind.String:
                snap.Kind = "String";
                snap.Data = raw as string ?? "";
                break;
            case RegistryValueKind.ExpandString:
                snap.Kind = "ExpandString";
                snap.Data = raw as string ?? "";
                break;
            case RegistryValueKind.Binary:
                snap.Kind = "Binary";
                snap.Data = Convert.ToBase64String(raw as byte[] ?? []);
                break;
            case RegistryValueKind.MultiString:
                snap.Kind = "MultiString";
                snap.Data = JsonSerializer.Serialize(raw as string[] ?? []);
                break;
            default:
                snap.Kind = "Missing";
                break;
        }
        return snap;
    }

    private static void Apply(RegistryValueSnapshot snap)
    {
        if (snap.Kind == "Missing")
        {
            using var opened = TryOpen(snap.Hive, snap.Key, writable: true);
            if (opened == null)
                return;
            if (opened.GetValueNames().Any(n => n.Equals(snap.Name, StringComparison.OrdinalIgnoreCase)))
                opened.DeleteValue(snap.Name, throwOnMissingValue: false);
            return;
        }

        using var key = Open(snap.Hive, snap.Key, writable: true);
        switch (snap.Kind)
        {
            case "DWord":
                key.SetValue(snap.Name, unchecked((int)uint.Parse(snap.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                break;
            case "QWord":
                key.SetValue(snap.Name, unchecked((long)ulong.Parse(snap.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture)), RegistryValueKind.QWord);
                break;
            case "String":
                key.SetValue(snap.Name, snap.Data ?? "", RegistryValueKind.String);
                break;
            case "ExpandString":
                key.SetValue(snap.Name, snap.Data ?? "", RegistryValueKind.ExpandString);
                break;
            case "Binary":
                key.SetValue(snap.Name, string.IsNullOrEmpty(snap.Data) ? [] : Convert.FromBase64String(snap.Data), RegistryValueKind.Binary);
                break;
            case "MultiString":
                var parts = string.IsNullOrEmpty(snap.Data) ? [] : JsonSerializer.Deserialize<string[]>(snap.Data) ?? [];
                key.SetValue(snap.Name, parts, RegistryValueKind.MultiString);
                break;
        }
    }

    private static RegistryKey Open(string hive, string key, bool writable)
        => Hive(hive).CreateSubKey(key, writable) ?? throw new InvalidOperationException("Clé de registre inaccessible : " + key);

    private static RegistryKey? TryOpen(string hive, string key, bool writable = false)
        => Hive(hive).OpenSubKey(key, writable);

    private static RegistryKey Hive(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Ruche inconnue.")
    };
}
