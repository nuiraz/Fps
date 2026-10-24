using System.Security.Cryptography;
using System.Text;
using GameBooster.Models;

namespace GameBooster.Services;

public sealed class LocalStore
{
    private readonly string _root;
    private readonly AppLog _log;

    public LocalStore(AppLog log)
    {
        _log = log;
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBooster");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
    }

    public string Root => _root;

    public MachineSettings LoadMachine()
    {
        var path = Path.Combine(_root, "machine-settings.json");
        try
        {
            if (!File.Exists(path))
                return new MachineSettings();
            var parsed = System.Text.Json.JsonSerializer.Deserialize<MachineSettings>(File.ReadAllText(path), AppJson.Options);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.ApiBaseUrl))
                return new MachineSettings();
            parsed.ApiBaseUrl = Guard.NormalizeApiBase(parsed.ApiBaseUrl);
            return parsed;
        }
        catch (Exception ex)
        {
            _log.Error("Lecture des réglages machine impossible", ex);
            return new MachineSettings();
        }
    }

    public void SaveMachine(MachineSettings settings)
    {
        settings.ApiBaseUrl = Guard.NormalizeApiBase(settings.ApiBaseUrl);
        var path = Path.Combine(_root, "machine-settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(settings, AppJson.Options));
    }

    public SessionData? LoadSession()
    {
        var path = Path.Combine(_root, "session.dat");
        var json = ReadProtected(path);
        if (json == null)
            return null;
        try
        {
            var session = System.Text.Json.JsonSerializer.Deserialize<SessionData>(json, AppJson.Options);
            if (session == null || string.IsNullOrWhiteSpace(session.Token) || string.IsNullOrWhiteSpace(session.Username))
                return null;
            return session;
        }
        catch (Exception ex)
        {
            _log.Error("Session locale illisible", ex);
            return null;
        }
    }

    public void SaveSession(SessionData session)
        => WriteProtected(Path.Combine(_root, "session.dat"), System.Text.Json.JsonSerializer.Serialize(session, AppJson.Options));

    public void ClearSession()
    {
        var path = Path.Combine(_root, "session.dat");
        if (File.Exists(path))
            File.Delete(path);
    }

    public MachineBackup LoadBackup()
    {
        var json = ReadProtected(Path.Combine(_root, "machine-backup.dat"));
        if (json == null)
            return new MachineBackup();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<MachineBackup>(json, AppJson.Options) ?? new MachineBackup();
        }
        catch (Exception ex)
        {
            _log.Error("Sauvegarde machine illisible", ex);
            return new MachineBackup();
        }
    }

    public void SaveBackup(MachineBackup backup)
        => WriteProtected(Path.Combine(_root, "machine-backup.dat"), System.Text.Json.JsonSerializer.Serialize(backup, AppJson.Options));

    public ProfileCache LoadProfile(string key)
    {
        var json = ReadProtected(ProfilePath(key));
        if (json == null)
            return new ProfileCache();
        try
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<ProfileCache>(json, AppJson.Options) ?? new ProfileCache();
            profile.Settings ??= SettingsDocument.CreateDefault();
            profile.Settings.Preferences ??= UserPreferences.CreateDefault();
            profile.Presets ??= [];
            profile.History ??= [];
            return profile;
        }
        catch (Exception ex)
        {
            _log.Error("Cache de profil illisible", ex);
            return new ProfileCache();
        }
    }

    public void SaveProfile(string key, ProfileCache profile)
        => WriteProtected(ProfilePath(key), System.Text.Json.JsonSerializer.Serialize(profile, AppJson.Options));

    private string ProfilePath(string key)
    {
        var safe = new string((key ?? "_local").ToLowerInvariant().Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
            safe = "_local";
        var dir = Path.Combine(_root, "cache", safe);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "profile.dat");
    }

    private string? ReadProtected(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _log.Error("Lecture DPAPI impossible : " + Path.GetFileName(path), ex);
            return null;
        }
    }

    private void WriteProtected(string path, string json)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }
}
