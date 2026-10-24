using Microsoft.Win32;

namespace GameBooster.Services;

public sealed class RestorePointService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string ValueName = "SystemRestorePointCreationFrequency";

    public string Create(string description)
    {
        int? previous = null;
        var existed = false;
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
            if (key?.GetValue(ValueName) is int number)
            {
                existed = true;
                previous = number;
            }
            key?.SetValue(ValueName, 0, RegistryValueKind.DWord);
        }
        catch
        {
            // La fréquence reste celle de Windows. La création peut quand même réussir.
        }

        try
        {
            var begin = new NativeMethods.RestorePointInfo
            {
                EventType = NativeMethods.BeginSystemChange,
                RestorePointType = NativeMethods.ModifySettings,
                Description = string.IsNullOrWhiteSpace(description) ? "Game Booster" : description
            };
            var ok = NativeMethods.SRSetRestorePointW(ref begin, out var status);
            if (!ok || status.Status != 0)
                return "Point de restauration non créé (" + Explain(status.Status) + "). Les réglages peuvent tout de même être appliqués.";

            var end = new NativeMethods.RestorePointInfo
            {
                EventType = NativeMethods.EndSystemChange,
                RestorePointType = NativeMethods.ModifySettings,
                SequenceNumber = status.SequenceNumber,
                Description = begin.Description
            };
            NativeMethods.SRSetRestorePointW(ref end, out _);
            return "Point de restauration créé.";
        }
        catch (Exception ex)
        {
            return "Point de restauration non créé (" + ex.Message + "). Les réglages peuvent tout de même être appliqués.";
        }
        finally
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
                if (key == null)
                {
                    // rien à remettre
                }
                else if (existed && previous != null)
                {
                    key.SetValue(ValueName, previous.Value, RegistryValueKind.DWord);
                }
                else if (!existed && key.GetValue(ValueName) is int current && current == 0)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // La fréquence à 0 autorise seulement des points plus rapprochés.
            }
        }
    }

    private static string Explain(int status) => status switch
    {
        1058 => "service de restauration arrêté",
        5 => "accès refusé",
        112 => "disque insuffisant",
        _ => "code " + status
    };
}
