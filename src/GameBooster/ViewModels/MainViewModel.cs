using System.Collections.ObjectModel;
using System.Windows.Threading;
using GameBooster.Models;
using GameBooster.Services;

namespace GameBooster.ViewModels;

public sealed class NavEntry : ObservableObject
{
    private bool _isSelected;
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Glyph { get; init; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public RelayCommand? Command { get; set; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private object _current;
    private string _syncText;
    private string _powerText = "Plan : …";

    public MainViewModel(AppServices services)
    {
        _services = services;
        Dashboard = new DashboardViewModel(services, OpenBoost);
        Boost = new BoostViewModel(services);
        Presets = new PresetsViewModel(services, OpenBoost);
        Network = new NetworkViewModel(services);
        Cleanup = new CleanupViewModel(services);
        Startup = new StartupViewModel(services);
        Settings = new SettingsViewModel(services);
        Account = new AccountViewModel(services, () => Navigate("dashboard"));
        _current = Dashboard;
        _syncText = services.State.SyncStatus;
        Nav = new ObservableCollection<NavEntry>(new NavEntry[]
        {
            Entry("dashboard", "Tableau de bord", "\uE80F"),
            Entry("boost", "Boost", "\uE945"),
            Entry("presets", "Presets", "\uE8FD"),
            Entry("network", "Réseau", "\uE774"),
            Entry("cleanup", "Nettoyage", "\uE74D"),
            Entry("startup", "Démarrage", "\uE7E8"),
            Entry("settings", "Paramètres", "\uE713"),
            Entry("account", "Compte", "\uE77B")
        });
        Nav[0].IsSelected = true;
        services.State.Changed += () =>
        {
            SyncText = services.State.SyncStatus;
            ShowLoginBanner = !services.State.IsLoggedIn;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => RefreshPower();
        _timer.Start();
        RefreshPower();
        ShowLoginBanner = !services.State.IsLoggedIn;
        _ = services.Sync.VerifyRememberedSessionAsync();
    }

    public DashboardViewModel Dashboard { get; }
    public BoostViewModel Boost { get; }
    public PresetsViewModel Presets { get; }
    public NetworkViewModel Network { get; }
    public CleanupViewModel Cleanup { get; }
    public StartupViewModel Startup { get; }
    public SettingsViewModel Settings { get; }
    public AccountViewModel Account { get; }
    public ObservableCollection<NavEntry> Nav { get; }
    public string AdminText => _services.IsAdmin ? "Administrateur" : "Droits insuffisants";
    public string VersionText { get; } = "  " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "");
    public string SyncText { get => _syncText; private set => Set(ref _syncText, value); }
    public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
    public bool ShowLoginBanner { get => _showBanner; private set => Set(ref _showBanner, value); }
    private bool _showBanner = true;
    public object Current { get => _current; private set => Set(ref _current, value); }
    public RelayCommand OpenAccountCommand => new(() => Navigate("account"));

    public void Navigate(string key)
    {
        Current = key switch
        {
            "boost" => Boost,
            "presets" => Presets,
            "network" => Network,
            "cleanup" => Cleanup,
            "startup" => Startup,
            "settings" => Settings,
            "account" => Account,
            _ => Dashboard
        };
        foreach (var item in Nav)
            item.IsSelected = item.Key == key || (key == "dashboard" && item.Key == "dashboard");
        if (key == "boost")
        {
            Boost.ReloadPresets();
            Boost.RefreshApps();
        }
        if (key == "startup") Startup.Reload();
        if (key == "presets") Presets.Reload();
        if (key == "settings") Settings.Reload();
        if (key == "account") Account.Reload();
        if (key == "dashboard") Dashboard.Reload();
        if (key == "network") Network.Reload();
    }

    private void OpenBoost(Preset preset)
    {
        Boost.LoadPreset(preset);
        Navigate("boost");
    }

    private NavEntry Entry(string key, string label, string glyph)
    {
        var entry = new NavEntry { Key = key, Label = label, Glyph = glyph };
        entry.Command = new RelayCommand(() => Navigate(key));
        return entry;
    }

    private void RefreshPower()
    {
        try
        {
            var active = _services.Power.GetActiveScheme();
            PowerText = "Plan : " + _services.Power.FrenchName(active);
        }
        catch
        {
            PowerText = "Plan : indisponible";
        }
    }
}
