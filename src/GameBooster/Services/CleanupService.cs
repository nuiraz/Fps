namespace GameBooster.Services;

public sealed class CleanupReport
{
    public int Files { get; set; }
    public long Bytes { get; set; }
    public List<string> Notes { get; } = [];
}

public sealed class CleanupService
{
    public CleanupReport CleanTemp() => CleanRoots(TempRoots(), "fichiers temporaires");

    public CleanupReport CleanShaderCaches() => CleanRoots(ShaderRoots(), "caches de shaders");

    public string EmptyRecycleBin()
    {
        var hr = NativeMethods.SHEmptyRecycleBin(
            IntPtr.Zero,
            null,
            NativeMethods.SherbNoConfirmation | NativeMethods.SherbNoProgressUi | NativeMethods.SherbNoSound);
        if (hr == 0 || hr == 1)
            return "Corbeille vidée. Les fichiers ne seront pas restaurés par « Restaurer par défaut ».";
        return "La corbeille n'a pas pu être vidée (code " + hr.ToString("X8") + ").";
    }

    private static CleanupReport CleanRoots(IEnumerable<string> roots, string label)
    {
        var report = new CleanupReport();
        var any = false;
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;
            any = true;
            CleanRoot(root, report);
        }
        if (!any)
            report.Notes.Add("Aucun dossier de " + label + " trouvé.");
        else
            report.Notes.Add(report.Files + " fichier(s) de " + label + " supprimé(s), " + FormatBytes(report.Bytes) + ".");
        return report;
    }

    private static void CleanRoot(string root, CleanupReport report)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", options).ToList();
        }
        catch (Exception ex)
        {
            report.Notes.Add(root + " : " + ex.Message);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                if (!IsUnder(root, file))
                    continue;
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                long length = 0;
                try { length = new FileInfo(file).Length; } catch { /* taille indisponible */ }
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                report.Files++;
                report.Bytes += length;
            }
            catch
            {
                // Fichier verrouillé : on passe au suivant.
            }
        }

        List<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root, "*", options)
                .OrderByDescending(path => path.Length)
                .ToList();
        }
        catch
        {
            return;
        }

        foreach (var dir in dirs)
        {
            try
            {
                if (!IsUnder(root, dir))
                    continue;
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, recursive: false);
            }
            catch
            {
                // Dossier encore occupé.
            }
        }
    }

    private static bool IsUnder(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> TempRoots()
    {
        yield return Path.GetTempPath();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
            yield return Path.Combine(windows, "Temp");
    }

    private static IEnumerable<string> ShaderRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            yield break;
        string[] relative =
        [
            "D3DSCache",
            Path.Combine("NVIDIA", "DXCache"),
            Path.Combine("NVIDIA", "GLCache"),
            Path.Combine("NVIDIA Corporation", "NV_Cache"),
            Path.Combine("AMD", "DxCache"),
            Path.Combine("AMD", "GLCache")
        ];
        foreach (var item in relative)
            yield return Path.Combine(local, item);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " o";
        double value = bytes / 1024d;
        if (value < 1024) return value.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Ko";
        value /= 1024d;
        return value.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo";
    }
}
