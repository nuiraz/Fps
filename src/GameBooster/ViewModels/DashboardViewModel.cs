using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using GameBooster.Models;
using GameBooster.Services;

namespace GameBooster.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<Preset> _openBoost;
    private readonly DispatcherTimer _timer;
    private readonly List<double> _samples = [];
    private IReadOnlyList<double> _sampleView = [];
    private double _cpu;
    private double _ram;
    private double _gpu;
    private string _cpuLabel = "—";
    private string _ramLabel = "—";
    private string _gpuLabel = "—";
    private string _tempLabel = "—";
    private string _ramDetail = "";
    private Brush _cpuBrush = Brushes.White;
    private Brush _ramBrush = Brushes.White;
    private Brush _gpuBrush = Brushes.White;

    public DashboardViewModel(AppServices services, Action<Preset> openBoost)
    {
        _services = services;
        _openBoost = openBoost;
        History = [];
        QuickBoostCommand = new AsyncRelayCommand(QuickBoost, () => !IsBusy);
        OpenAccountCommand = new RelayCommand(() => { });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        services.State.Changed += Reload;
        Reload();
        Sample();
    }

    public ObservableCollection<string> History { get; }
    public AsyncRelayCommand QuickBoostCommand { get; }
    public RelayCommand OpenAccountCommand { get; }
    public double Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public double Ram { get => _ram; private set => Set(ref _ram, value); }
    public double Gpu { get => _gpu; private set => Set(ref _gpu, value); }
    public string CpuLabel { get => _cpuLabel; private set => Set(ref _cpuLabel, value); }
    public string RamLabel { get => _ramLabel; private set => Set(ref _ramLabel, value); }
    public string GpuLabel { get => _gpuLabel; private set => Set(ref _gpuLabel, value); }
    public string TempLabel { get => _tempLabel; private set => Set(ref _tempLabel, value); }
    public string RamDetail { get => _ramDetail; private set => Set(ref _ramDetail, value); }
    public IReadOnlyList<double> Samples => _sampleView;
    public Brush CpuBrush { get => _cpuBrush; private set => Set(ref _cpuBrush, value); }
    public Brush RamBrush { get => _ramBrush; private set => Set(ref _ramBrush, value); }
    public Brush GpuBrush { get => _gpuBrush; private set => Set(ref _gpuBrush, value); }
    public bool IsBusy { get; private set; }
    public string ActivePresetName { get; private set; } = "";

    public void Reload()
    {
        History.Clear();
        foreach (var entry in _services.State.Profile.History.Take(8))
        {
            var mark = entry.Success ? "OK" : "Échec";
            History.Add(TimeStamp.Local(entry.OccurredAt) + "  " + mark + "  " + entry.Detail);
        }
        if (History.Count == 0)
            History.Add("Aucun boost enregistré pour ce profil.");
        var id = _services.State.Profile.Settings.Preferences.ActivePresetId;
        ActivePresetName = Resolve(id)?.Name ?? "Compétitif / Fortnite";
        Raise(nameof(ActivePresetName));
    }

    private async Task QuickBoost()
    {
        var preset = Resolve(_services.State.Profile.Settings.Preferences.ActivePresetId) ?? PresetCatalog.BuiltIns[0];
        if (!Dialogs.Confirm("Lancer « " + preset.Name + " » maintenant ? Les fichiers supprimés ne seront pas restaurés."))
            return;
        IsBusy = true;
        try
        {
            var apps = _services.State.Profile.Settings.Preferences.PrecheckBackgroundApps.ToArray();
            await _services.Tweaks.ApplyAsync(preset.Tweaks.Clone(), apps, preset.Id, preset.Name);
            Reload();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Preset? Resolve(string? id)
    {
        var builtIn = PresetCatalog.Find(id);
        if (builtIn != null)
            return builtIn;
        return _services.State.Profile.Presets.FirstOrDefault(p => p.Id == id && !p.Deleted);
    }

    private void Sample()
    {
        var snap = _services.Metrics.Latest;
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        Cpu = snap.Cpu;
        Ram = snap.RamPercent;
        Gpu = snap.Gpu ?? 0;
        CpuLabel = snap.Cpu.ToString("0", culture) + " %";
        RamLabel = snap.RamPercent.ToString("0", culture) + " %";
        GpuLabel = snap.Gpu == null ? "—" : snap.Gpu.Value.ToString("0", culture) + " %";
        TempLabel = snap.GpuTemp == null ? "Température GPU —" : "GPU " + snap.GpuTemp.Value.ToString("0", culture) + " °C";
        RamDetail = snap.RamTotalGb <= 0
            ? ""
            : snap.RamUsedGb.ToString("0.0", culture) + " / " + snap.RamTotalGb.ToString("0.0", culture) + " Go";
        CpuBrush = BrushFor(snap.Cpu);
        RamBrush = BrushFor(snap.RamPercent);
        GpuBrush = snap.Gpu == null ? (Brush)AppBrush("Brush.Faint") : BrushFor(snap.Gpu.Value);
        _samples.Add(snap.Cpu);
        if (_samples.Count > 40)
            _samples.RemoveAt(0);
        _sampleView = _samples.ToArray();
        Raise(nameof(Samples));
    }

    private Brush BrushFor(double value)
    {
        if (value >= 90) return (Brush)AppBrush("Brush.Coral");
        if (value >= 75) return (Brush)AppBrush("Brush.Warn");
        return (Brush)AppBrush("Brush.Lime");
    }

    private static object AppBrush(string key) => System.Windows.Application.Current.FindResource(key);
}
