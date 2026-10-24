using System.Collections.ObjectModel;
using System.Text.Json;
using GameBooster.Models;
using GameBooster.Services;
using Microsoft.Win32;

namespace GameBooster.ViewModels;

public sealed class PresetsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<Preset> _apply;
    private Preset? _selected;
    private string _name = "";
    private string _description = "";
    private TweakSelection _draft = new();

    public PresetsViewModel(AppServices services, Action<Preset> apply)
    {
        _services = services;
        _apply = apply;
        BuiltIns = new ObservableCollection<Preset>(PresetCatalog.BuiltIns);
        Customs = [];
        NewCommand = new RelayCommand(Create);
        SaveCommand = new RelayCommand(Save, () => Selected != null);
        DeleteCommand = new RelayCommand(Delete, () => Selected != null);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export);
        ApplyBuiltInCommand = new RelayCommand(p => { if (p is Preset preset) _apply(preset); });
        ApplyCustomCommand = new RelayCommand(ApplyCustom, () => Selected != null);
        Reload();
    }

    public ObservableCollection<Preset> BuiltIns { get; }
    public ObservableCollection<Preset> Customs { get; }
    public Preset? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;
            if (value == null)
                return;
            Name = value.Name;
            Description = value.Description;
            Draft = value.Tweaks.Clone();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public TweakSelection Draft { get => _draft; private set => Set(ref _draft, value); }
    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ApplyBuiltInCommand { get; }
    public RelayCommand ApplyCustomCommand { get; }

    public void Reload()
    {
        var id = _selected?.Id;
        Customs.Clear();
        foreach (var preset in _services.State.Profile.Presets.Where(p => !p.BuiltIn && !p.Deleted))
            Customs.Add(preset);
        Selected = Customs.FirstOrDefault(p => p.Id == id) ?? Customs.FirstOrDefault();
    }

    private void Create()
    {
        var now = TimeStamp.Touch();
        var preset = new Preset
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = "Nouveau preset",
            Description = "",
            Tweaks = new TweakSelection { CreateRestorePoint = true },
            CreatedAt = now,
            UpdatedAt = now
        };
        _services.State.Profile.Presets.Add(preset);
        _selected = preset;
        _services.Sync.SaveProfile();
        Reload();
    }

    private void Save() => SaveCore(announce: true);

    private bool SaveCore(bool announce)
    {
        if (Selected == null)
            return false;
        var name = (Name ?? "").Trim();
        if (name.Length < 1 || name.Length > 60)
        {
            Dialogs.Error("Le nom doit contenir entre 1 et 60 caractères.");
            return false;
        }
        Selected.Name = name;
        Selected.Description = (Description ?? "").Trim();
        if (Selected.Description.Length > 300)
            Selected.Description = Selected.Description[..300];
        Selected.Tweaks = Draft.Clone();
        Selected.UpdatedAt = TimeStamp.Touch(Selected.UpdatedAt);
        _services.Sync.SaveProfile();
        Reload();
        if (announce)
            Dialogs.Info("Preset enregistré. Il sera envoyé au compte si vous êtes connecté.");
        return true;
    }

    private void Delete()
    {
        if (Selected == null)
            return;
        if (!Dialogs.Confirm("Supprimer « " + Selected.Name + " » de ce compte ?"))
            return;
        Selected.Deleted = true;
        Selected.UpdatedAt = TimeStamp.Touch(Selected.UpdatedAt);
        _services.Sync.SaveProfile();
        Reload();
    }

    private void ApplyCustom()
    {
        if (Selected == null || !SaveCore(announce: false))
            return;
        _apply(Selected);
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Presets Game Booster (*.json)|*.json",
            Title = "Importer des presets"
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 1024 * 1024)
            {
                Dialogs.Error("Le fichier dépasse 1 Mo.");
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var format) || format.GetString() != "gamebooster-presets")
            {
                Dialogs.Error("Format attendu : gamebooster-presets version 1.");
                return;
            }
            if (!root.TryGetProperty("presets", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                Dialogs.Error("Aucun preset dans ce fichier.");
                return;
            }
            var count = 0;
            foreach (var node in list.EnumerateArray())
            {
                var preset = node.Deserialize<Preset>(AppJson.Options);
                if (preset == null || string.IsNullOrWhiteSpace(preset.Name))
                    continue;
                preset.Id = Guid.NewGuid().ToString("D");
                preset.BuiltIn = false;
                preset.Deleted = false;
                preset.Name = preset.Name.Trim();
                if (preset.Name.Length > 60)
                    preset.Name = preset.Name[..60];
                preset.Description = (preset.Description ?? "").Trim();
                preset.Tweaks ??= new TweakSelection();
                var now = TimeStamp.Touch();
                preset.CreatedAt = now;
                preset.UpdatedAt = now;
                _services.State.Profile.Presets.Add(preset);
                count++;
            }
            _services.Sync.SaveProfile();
            Reload();
            Dialogs.Info(count + " preset(s) importé(s) avec de nouveaux identifiants.");
        }
        catch (Exception ex)
        {
            Faults.Report(ex, "Import de presets");
        }
    }

    private void Export()
    {
        var presets = _services.State.Profile.Presets.Where(p => !p.BuiltIn && !p.Deleted).Select(p => new
        {
            name = p.Name,
            description = p.Description,
            tweaks = p.Tweaks
        }).ToList();
        if (presets.Count == 0)
        {
            Dialogs.Info("Aucun preset personnel à exporter. Les presets intégrés restent dans l'application.");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "Presets Game Booster (*.json)|*.json",
            FileName = "gamebooster-presets.json",
            Title = "Exporter les presets"
        };
        if (dialog.ShowDialog() != true)
            return;
        var payload = new { format = "gamebooster-presets", version = 1, presets };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, AppJson.Options));
        Dialogs.Info("Export terminé.");
    }
}
