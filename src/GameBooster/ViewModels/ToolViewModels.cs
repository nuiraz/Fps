using System.Collections.ObjectModel;
using System.Text;
using GameBooster.Models;
using GameBooster.Services;

namespace GameBooster.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _host = "1.1.1.1";
    private int _count = 4;
    private string _output = "Le test ICMP mesure un ping, pas le lag d'une partie.";
    private bool _busy;

    public NetworkViewModel(AppServices services)
    {
        _services = services;
        ApplyOffCommand = new AsyncRelayCommand(() => Apply(ToggleAction.Disable), () => !IsBusy);
        ApplyOnCommand = new AsyncRelayCommand(() => Apply(ToggleAction.Enable), () => !IsBusy);
        FlushCommand = new AsyncRelayCommand(Flush, () => !IsBusy);
        PingCommand = new AsyncRelayCommand(Ping, () => !IsBusy);
        Reload();
    }

    public string Host { get => _host; set => Set(ref _host, value); }
    public int Count { get => _count; set => Set(ref _count, value); }
    public string Output { get => _output; private set => Set(ref _output, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public AsyncRelayCommand ApplyOffCommand { get; }
    public AsyncRelayCommand ApplyOnCommand { get; }
    public AsyncRelayCommand FlushCommand { get; }
    public AsyncRelayCommand PingCommand { get; }

    public void Reload()
    {
        Host = _services.State.Profile.Settings.Preferences.PingHost;
        Count = _services.State.Profile.Settings.Preferences.PingCount;
    }

    private async Task Apply(ToggleAction action)
    {
        IsBusy = true;
        try
        {
            var selection = new TweakSelection { Nagle = action, CreateRestorePoint = false };
            var lines = await _services.Tweaks.ApplyAsync(selection, [], null, null, "network");
            Output = string.Join(Environment.NewLine, lines);
        }
        finally { IsBusy = false; }
    }

    private async Task Flush()
    {
        IsBusy = true;
        try
        {
            var line = await Task.Run(_services.Network.FlushDns);
            _services.Tweaks.AddHistory("network", line, true);
            Output = line;
        }
        finally { IsBusy = false; }
    }

    private async Task Ping()
    {
        var host = (Host ?? "").Trim();
        if (!PreferenceRules.IsHost(host))
        {
            Output = "Hôte de ping invalide. Utilisez un nom ou une adresse, sans espace ni schéma.";
            return;
        }
        var count = Math.Clamp(Count, 1, 10);
        var prefs = _services.State.Profile.Settings.Preferences;
        if (!string.Equals(prefs.PingHost, host, StringComparison.Ordinal) || prefs.PingCount != count)
        {
            prefs.PingHost = host;
            prefs.PingCount = count;
            _services.State.Profile.Settings.UpdatedAt = TimeStamp.Touch(_services.State.Profile.Settings.UpdatedAt);
            _services.Sync.SaveProfile();
        }
        IsBusy = true;
        try
        {
            var lines = await _services.Network.PingAsync(prefs.PingHost, prefs.PingCount, CancellationToken.None);
            Output = string.Join(Environment.NewLine, lines);
            _services.Tweaks.AddHistory("network", "Test ICMP vers " + prefs.PingHost, true);
        }
        catch (Exception ex)
        {
            Output = ex.Message;
        }
        finally { IsBusy = false; }
    }
}

public sealed class CleanupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _temp = true;
    private bool _shaders;
    private bool _recycle;
    private bool _busy;
    private string _output = "Le nettoyage est définitif. « Restaurer par défaut » ne ramène pas ces fichiers.";

    public CleanupViewModel(AppServices services)
    {
        _services = services;
        RunCommand = new AsyncRelayCommand(Run, () => !IsBusy && (CleanTemp || CleanShaders || EmptyRecycle));
    }

    public bool CleanTemp { get => _temp; set { if (Set(ref _temp, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool CleanShaders { get => _shaders; set { if (Set(ref _shaders, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool EmptyRecycle { get => _recycle; set { if (Set(ref _recycle, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string Output { get => _output; private set => Set(ref _output, value); }
    public AsyncRelayCommand RunCommand { get; }

    private async Task Run()
    {
        if (!Dialogs.Confirm("Supprimer les éléments cochés ? Cette action est définitive."))
            return;
        IsBusy = true;
        try
        {
            var selection = new TweakSelection
            {
                CleanTemp = CleanTemp,
                CleanShaderCache = CleanShaders,
                EmptyRecycleBin = EmptyRecycle,
                CreateRestorePoint = false
            };
            var lines = await _services.Tweaks.ApplyAsync(selection, [], null, null, "cleanup");
            Output = string.Join(Environment.NewLine, lines);
        }
        finally { IsBusy = false; }
    }
}

public sealed class StartupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _status = "Les interrupteurs modifient StartupApproved. La sauvegarde d'origine reste sur ce PC.";

    public StartupViewModel(AppServices services)
    {
        _services = services;
        Items = [];
        ToggleCommand = new RelayCommand(p => { if (p is StartupItem item) Toggle(item); });
        RefreshCommand = new RelayCommand(Reload);
    }

    public ObservableCollection<StartupItem> Items { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public RelayCommand ToggleCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public void Reload()
    {
        try
        {
            var items = _services.Startup.List();
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            Status = items.Count + " entrée(s).";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private void Toggle(StartupItem item)
    {
        try
        {
            string line;
            lock (_services.State.Gate)
            {
                line = _services.Startup.SetEnabled(item, !item.Enabled, _services.State.Backup);
                _services.Store.SaveBackup(_services.State.Backup);
            }
            _services.Tweaks.AddHistory("startup", line, true);
            Status = line;
            Reload();
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
        }
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _apiUrl = "";
    private bool _remember;
    private string _games = "";
    private string _paths = "";
    private string _pingHost = "1.1.1.1";
    private int _pingCount = 4;
    private string _health = "L'URL de l'API reste sur cet ordinateur.";
    private Preset? _active;
    private bool _busy;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Presets = [];
        SaveCommand = new RelayCommand(Save);
        TestCommand = new AsyncRelayCommand(Test, () => !IsBusy);
        Reload();
    }

    public ObservableCollection<Preset> Presets { get; }
    public string ApiUrl { get => _apiUrl; set => Set(ref _apiUrl, value); }
    public bool RememberSession { get => _remember; set => Set(ref _remember, value); }
    public string Games { get => _games; set => Set(ref _games, value); }
    public string Paths { get => _paths; set => Set(ref _paths, value); }
    public string PingHost { get => _pingHost; set => Set(ref _pingHost, value); }
    public int PingCount { get => _pingCount; set => Set(ref _pingCount, value); }
    public string Health { get => _health; private set => Set(ref _health, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public Preset? ActivePreset { get => _active; set => Set(ref _active, value); }
    public RelayCommand SaveCommand { get; }
    public AsyncRelayCommand TestCommand { get; }
    public bool Placeholder => Guard.IsPlaceholder(ApiUrl);

    public void Reload()
    {
        ApiUrl = _services.State.Machine.ApiBaseUrl;
        RememberSession = _services.State.Machine.RememberSession;
        var prefs = _services.State.Profile.Settings.Preferences;
        Games = string.Join(Environment.NewLine, prefs.WatchedGames);
        Paths = string.Join(Environment.NewLine, prefs.FullscreenOptimizationPaths);
        PingHost = prefs.PingHost;
        PingCount = prefs.PingCount;
        var id = ActivePreset?.Id;
        Presets.Clear();
        foreach (var preset in PresetCatalog.BuiltIns)
            Presets.Add(preset);
        foreach (var preset in _services.State.Profile.Presets.Where(p => !p.Deleted && !p.BuiltIn))
            Presets.Add(preset);
        ActivePreset = Presets.FirstOrDefault(p => p.Id == (id ?? prefs.ActivePresetId)) ?? Presets.FirstOrDefault();
        Raise(nameof(Placeholder));
    }

    private void Save()
    {
        var games = Split(Games).Where(PreferenceRules.IsProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
        var paths = Split(Paths).Select(PreferenceRules.CanonicalExe).Where(path => path != null).Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        var host = (PingHost ?? "").Trim();
        if (!PreferenceRules.IsHost(host))
        {
            Dialogs.Error("Hôte de ping invalide. Utilisez un nom ou une adresse, sans espace ni schéma.");
            return;
        }
        _services.State.Machine.ApiBaseUrl = Guard.NormalizeApiBase(ApiUrl);
        _services.State.Machine.RememberSession = RememberSession;
        _services.Store.SaveMachine(_services.State.Machine);
        if (!RememberSession)
            _services.Store.ClearSession();
        else if (_services.State.Session != null)
            _services.Store.SaveSession(_services.State.Session);

        var prefs = _services.State.Profile.Settings.Preferences;
        prefs.WatchedGames = games;
        prefs.FullscreenOptimizationPaths = paths;
        prefs.PingHost = host;
        prefs.PingCount = Math.Clamp(PingCount, 1, 10);
        prefs.ActivePresetId = ActivePreset?.Id;
        _services.State.Profile.Settings.UpdatedAt = TimeStamp.Touch(_services.State.Profile.Settings.UpdatedAt);
        _services.Sync.SaveProfile();
        Reload();
        Health = Placeholder
            ? "L'URL contient encore <subdomain>. Remplacez-la par l'adresse du Worker avant de vous connecter."
            : "Réglages enregistrés. Les préférences du compte partent au nuage à la prochaine synchro.";
    }

    private async Task Test()
    {
        Save();
        if (Guard.IsPlaceholder(_services.State.Machine.ApiBaseUrl))
        {
            Health = "Adresse d'exemple : remplacez <subdomain> dans l'URL.";
            return;
        }
        IsBusy = true;
        try
        {
            var ok = await _services.Api.HealthAsync();
            Health = ok ? "API joignable." : "L'API a répondu sans confirmer /api/health.";
        }
        catch (Exception ex)
        {
            Health = ErrorText.French(ex);
        }
        finally { IsBusy = false; }
    }

    private static IEnumerable<string> Split(string? text)
        => (text ?? "").Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class AccountViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _continue;
    private string _username = "";
    private string _message = "Les presets, préférences et l'historique de boost vivent dans le compte. Ce PC ne garde qu'un cache chiffré DPAPI.";
    private bool _busy;

    public AccountViewModel(AppServices services, Action goDashboard)
    {
        _services = services;
        _continue = goDashboard;
        LoginCommand = new AsyncRelayCommand(p => Submit(p as string, false), _ => !IsBusy);
        RegisterCommand = new AsyncRelayCommand(p => Submit(p as string, true), _ => !IsBusy);
        LogoutCommand = new AsyncRelayCommand(Logout, () => !IsBusy && IsLoggedIn);
        SyncCommand = new AsyncRelayCommand(() => services.Sync.SyncNowAsync(), () => !IsBusy && IsLoggedIn);
        ContinueCommand = new RelayCommand(goDashboard);
        services.State.Changed += Reload;
        Reload();
    }

    public string Username { get => _username; set => Set(ref _username, value); }
    public bool RememberSession
    {
        get => _services.State.Machine.RememberSession;
        set
        {
            _services.State.Machine.RememberSession = value;
            _services.Store.SaveMachine(_services.State.Machine);
            Raise();
        }
    }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool IsLoggedIn => _services.State.IsLoggedIn;
    public string Identity => _services.State.Session == null
        ? "Non connecté"
        : _services.State.Session.Username + "  ·  " + _services.State.Session.UserId;
    public string SyncLine => _services.State.SyncStatus + (string.IsNullOrWhiteSpace(_services.State.LastSyncError) ? "" : " — " + _services.State.LastSyncError);
    public string CacheLine => "Dernier échange : " + TimeStamp.Local(_services.State.Profile.LastSyncAt);
    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public AsyncRelayCommand SyncCommand { get; }
    public RelayCommand ContinueCommand { get; }

    public void Reload()
    {
        Raise(nameof(IsLoggedIn));
        Raise(nameof(Identity));
        Raise(nameof(SyncLine));
        Raise(nameof(CacheLine));
        Raise(nameof(RememberSession));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task Submit(string? password, bool register)
    {
        IsBusy = true;
        try
        {
            var error = await _services.Sync.LoginAsync(Username, password ?? "", register, RememberSession);
            Message = error ?? (register ? "Compte créé et synchronisé." : "Connecté. Les données du compte ont été synchronisées.");
            if (error == null)
                _continue();
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    private async Task Logout()
    {
        IsBusy = true;
        try
        {
            await _services.Sync.LogoutAsync();
            Message = "Déconnecté. Le cache du compte reste sur ce PC, la session non. La sauvegarde des tweaks n'a pas été envoyée.";
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }
}
