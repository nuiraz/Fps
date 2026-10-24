using System.Collections.ObjectModel;
using System.Text;
using GameBooster.Models;
using GameBooster.Services;

namespace GameBooster.ViewModels;

public sealed class BoostViewModel : ObservableObject
{
    private readonly AppServices _services;
    private bool _loadingChecks;
    private Preset? _selected;
    private TweakSelection _working = new();
    private string _log = "En attente d'un boost.";
    private bool _busy;

    public BoostViewModel(AppServices services)
    {
        _services = services;
        Presets = [];
        Apps = new ObservableCollection<CloseableAppItem>(AppCatalog.CreateAppList());
        foreach (var app in Apps)
        {
            app.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CloseableAppItem.IsSelected))
                    RememberChecks();
            };
        }
        BoostCommand = new AsyncRelayCommand(Apply, () => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(Restore, () => !IsBusy);
        RefreshCommand = new RelayCommand(RefreshApps);
        DetectCommand = new RelayCommand(DetectFortnite);
        ReloadPresets();
        if (SelectedPreset != null)
            Working = SelectedPreset.Tweaks.Clone();
        RefreshApps();
    }

    public ObservableCollection<Preset> Presets { get; }
    public ObservableCollection<CloseableAppItem> Apps { get; }
    public Preset? SelectedPreset
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value) || value == null)
                return;
            Working = value.Tweaks.Clone();
        }
    }
    public TweakSelection Working { get => _working; private set => Set(ref _working, value); }
    public string Log { get => _log; private set => Set(ref _log, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public AsyncRelayCommand BoostCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand DetectCommand { get; }

    public void LoadPreset(Preset preset)
    {
        ReloadPresets();
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == preset.Id) ?? preset;
        Working = preset.Tweaks.Clone();
    }

    public void RefreshApps()
    {
        _loadingChecks = true;
        try
        {
            _services.Processes.RefreshRunning(Apps);
            var chosen = _services.State.Profile.Settings.Preferences.PrecheckBackgroundApps.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var app in Apps)
                app.IsSelected = chosen.Contains(app.ProcessName);
        }
        finally
        {
            _loadingChecks = false;
        }
    }

    public void ReloadPresets()
    {
        var id = _selected?.Id;
        Presets.Clear();
        foreach (var preset in PresetCatalog.BuiltIns)
            Presets.Add(preset);
        foreach (var preset in _services.State.Profile.Presets.Where(p => !p.Deleted && !p.BuiltIn))
            Presets.Add(preset);
        _selected = Presets.FirstOrDefault(p => p.Id == id) ?? Presets.FirstOrDefault();
        Raise(nameof(SelectedPreset));
    }

    private async Task Apply()
    {
        if (!Dialogs.Confirm("Appliquer ces réglages ? Un point de restauration est créé si l'option est cochée. Le nettoyage de fichiers est définitif."))
            return;
        IsBusy = true;
        try
        {
            var lines = await _services.Tweaks.ApplyAsync(Working.Clone(), SelectedApps(), SelectedPreset?.Id, SelectedPreset?.Name);
            Log = Join(lines);
        }
        finally
        {
            IsBusy = false;
            RefreshApps();
        }
    }

    private async Task Restore()
    {
        if (!Dialogs.Confirm("Restaurer les valeurs d'origine enregistrées sur ce PC ? Les fichiers supprimés (temporaires, shaders, corbeille) ne reviendront pas."))
            return;
        IsBusy = true;
        try
        {
            var lines = await _services.Tweaks.RestoreAsync();
            Log = Join(lines);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DetectFortnite()
    {
        var exe = PresetCatalog.DetectFortniteExe();
        if (exe == null)
        {
            Dialogs.Info("Fortnite n'a pas été trouvé dans les métadonnées du lanceur Epic. Ajoutez l'exécutable dans Paramètres si besoin. Aucun fichier du jeu n'est modifié.");
            return;
        }
        var prefs = _services.State.Profile.Settings.Preferences;
        if (!prefs.FullscreenOptimizationPaths.Contains(exe, StringComparer.OrdinalIgnoreCase))
        {
            prefs.FullscreenOptimizationPaths.Add(exe);
            _services.State.Profile.Settings.UpdatedAt = TimeStamp.Now();
            _services.Sync.SaveProfile();
        }
        Dialogs.Info("Exécutable ajouté pour les optimisations plein écran :\n" + exe);
    }

    private void RememberChecks()
    {
        if (_loadingChecks)
            return;
        var prefs = _services.State.Profile.Settings.Preferences;
        prefs.PrecheckBackgroundApps = Apps.Where(a => a.IsSelected).Select(a => a.ProcessName).ToList();
        _services.State.Profile.Settings.UpdatedAt = TimeStamp.Now();
        _services.Sync.SaveProfile();
    }

    private string[] SelectedApps() => Apps.Where(a => a.IsSelected).Select(a => a.ProcessName).ToArray();

    private static string Join(IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            builder.AppendLine("• " + line);
        return builder.Length == 0 ? "Terminé." : builder.ToString();
    }
}
