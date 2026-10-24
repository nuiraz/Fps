using System.Diagnostics;
using System.Net.NetworkInformation;
using GameBooster.Models;
using Microsoft.Win32;

namespace GameBooster.Services;

public sealed class NetworkService
{
    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string ProfileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    public List<string> ApplyNagle(ToggleAction action, MachineBackup backup)
    {
        var lines = new List<string>();
        if (action == ToggleAction.Leave)
        {
            lines.Add("Réglages réseau laissés tels quels.");
            return lines;
        }

        var targets = ListTargets();
        foreach (var (key, name) in targets)
            RegistryJournal.Remember(backup, "HKLM", key, name);

        if (action == ToggleAction.Disable)
        {
            foreach (var (key, name) in targets)
            {
                var value = name == "NetworkThrottlingIndex" ? 0xFFFFFFFFu : 1u;
                RegistryJournal.SetDword("HKLM", key, name, value);
            }
            lines.Add("Nagle désactivé sur " + targets.Count(t => t.Name == "TCPNoDelay") + " interface(s). La limitation multimédia est levée.");
            return lines;
        }

        foreach (var (key, name) in targets)
            RegistryJournal.WriteBackIfPresent(backup, "HKLM", key, name);
        lines.Add("Nagle rétabli selon les valeurs d'origine enregistrées sur ce PC.");
        return lines;
    }

    public string FlushDns()
    {
        var info = new ProcessStartInfo
        {
            FileName = "ipconfig",
            Arguments = "/flushdns",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("ipconfig introuvable.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        return process.ExitCode == 0
            ? "Cache DNS vidé."
            : "ipconfig a renvoyé le code " + process.ExitCode + ".";
    }

    public async Task<List<string>> PingAsync(string host, int count, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "La latence ICMP n'est pas le lag en jeu : celui-ci dépend du FAI, du routage et des serveurs du jeu."
        };
        var target = (host ?? "").Trim();
        if (!PreferenceRules.IsHost(target))
        {
            lines.Add("Hôte de test invalide.");
            return lines;
        }
        count = Math.Clamp(count, 1, 10);
        using var ping = new Ping();
        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromSeconds(2));
                lines.Add(reply.Status == IPStatus.Success
                    ? i + ". " + target + " : " + reply.RoundtripTime + " ms"
                    : i + ". " + target + " : " + reply.Status);
            }
            catch (Exception ex)
            {
                lines.Add(i + ". " + ex.Message);
            }
        }
        return lines;
    }

    private static List<(string Key, string Name)> ListTargets()
    {
        var targets = new List<(string, string)> { (ProfileKey, "NetworkThrottlingIndex") };
        using var root = Registry.LocalMachine.OpenSubKey(InterfacesKey);
        if (root == null)
            return targets;
        foreach (var sub in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(sub);
            if (key == null || !HasRealIp(key))
                continue;
            var path = InterfacesKey + "\\" + sub;
            targets.Add((path, "TcpAckFrequency"));
            targets.Add((path, "TCPNoDelay"));
        }
        return targets;
    }

    private static bool HasRealIp(RegistryKey key)
    {
        var dhcp = key.GetValue("DhcpIPAddress") as string;
        if (IsReal(dhcp))
            return true;
        if (key.GetValue("IPAddress") is string[] list && list.Any(IsReal))
            return true;
        return false;
    }

    private static bool IsReal(string? ip)
        => !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0" && ip != "::";
}
