using System.Diagnostics;
using System.Text.RegularExpressions;
using GameBooster.Models;

namespace GameBooster.Services;

public sealed partial class PowerPlanService
{
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string High = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private string? _cachedScheme;
    private long _cachedSchemeAt;

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    public string? GetActiveScheme()
    {
        var now = Environment.TickCount64;
        if (_cachedSchemeAt != 0 && now - _cachedSchemeAt < 5000)
            return _cachedScheme;
        var output = Run("/getactivescheme");
        var match = GuidPattern().Match(output);
        _cachedScheme = match.Success ? match.Value.ToLowerInvariant() : null;
        _cachedSchemeAt = now;
        return _cachedScheme;
    }

    public IReadOnlyList<string> ListSchemes()
    {
        var output = Run("/list");
        return GuidPattern().Matches(output).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
    }

    public string FrenchName(string? guid)
    {
        return (guid ?? "").ToLowerInvariant() switch
        {
            Ultimate => "Performances ultimes",
            High => "Performances élevées",
            Balanced => "Utilisation normale",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "Économie d'énergie",
            "" => "Inconnu",
            _ => "Plan personnalisé"
        };
    }

    public string Apply(PowerPlanChoice choice, MachineBackup backup)
    {
        if (choice == PowerPlanChoice.Leave)
            return "Plan d'alimentation laissé tel quel.";

        backup.PreviousPowerScheme ??= GetActiveScheme();
        var schemes = ListSchemes();
        string guid;
        var note = "";
        if (choice == PowerPlanChoice.Ultimate)
        {
            if (!schemes.Contains(Ultimate))
            {
                var created = TryDuplicateUltimate();
                if (created == null)
                {
                    guid = High;
                    note = " Le mode Performances ultimes n'est pas disponible, activation de Performances élevées.";
                }
                else
                {
                    guid = created;
                }
            }
            else
            {
                guid = Ultimate;
            }
        }
        else if (choice == PowerPlanChoice.High)
        {
            guid = High;
        }
        else
        {
            guid = Balanced;
        }

        if (!GuidPattern().IsMatch(guid))
            throw new InvalidOperationException("Identifiant de plan d'alimentation invalide.");
        Run("/setactive " + guid);
        _cachedScheme = guid.ToLowerInvariant();
        _cachedSchemeAt = Environment.TickCount64;
        return "Plan d'alimentation : " + FrenchName(guid) + "." + note;
    }

    public string? Restore(MachineBackup backup)
    {
        if (string.IsNullOrWhiteSpace(backup.PreviousPowerScheme))
            return null;
        var guid = backup.PreviousPowerScheme.ToLowerInvariant();
        if (!GuidPattern().IsMatch(guid))
            return "L'ancien plan d'alimentation enregistré est illisible.";
        Run("/setactive " + guid);
        _cachedScheme = guid;
        _cachedSchemeAt = Environment.TickCount64;
        var name = FrenchName(guid);
        backup.PreviousPowerScheme = null;
        return "Plan d'alimentation restauré : " + name + ".";
    }

    private string? TryDuplicateUltimate()
    {
        var output = Run("-duplicatescheme " + Ultimate);
        var match = GuidPattern().Matches(output).LastOrDefault();
        if (match == null || !match.Success)
            return null;
        var guid = match.Value.ToLowerInvariant();
        return guid == Ultimate ? ListSchemes().FirstOrDefault(g => g != Ultimate) ?? guid : guid;
    }

    private static string Run(string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("powercfg introuvable.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("powercfg a échoué (code " + process.ExitCode + ").");
        return stdout + "\n" + stderr;
    }
}
