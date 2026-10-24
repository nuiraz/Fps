using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows.Media;
using System.Windows.Threading;
using GameBooster.Models;
using GameBooster.Services;
using Microsoft.Win32;

namespace GameBooster.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;
    private readonly List<double> _cpuSamples = [];
    private readonly List<double> _ramSamples = [];
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("fr-FR");
    private int _tick;
    private int _pingBusy;
    private int _statusBusy;
    private double _cpu;
    private double _ram;
    private double _gpu;
    private double _disk;
    private double _score;
    private double _boostProgress;
    private bool _busy;
    private string _cpuLabel = "—";
    private string _ramLabel = "—";
    private string _gpuLabel = "—";
    private string _diskLabel = "—";
    private string _scoreLabel = "—";
    private string _cpuDetail = "";
    private string _ramDetail = "";
    private string _gpuDetail = "";
    private string _diskDetail = "";
    private string _scoreDetail = "Avant le boost";
    private string _gameModeText = "…";
    private string _powerPlanText = "…";
    private string _pingText = "…";
    private string _boostStatus = "";
    private IReadOnlyList<double> _cpuView = [];
    private IReadOnlyList<double> _ramView = [];
    private Brush _cpuBrush = Brushes.White;
    private Brush _ramBrush = Brushes.White;
    private Brush _gpuBrush = Brushes.White;
    private Brush _diskBrush = Brushes.White;
    private Brush _scoreBrush = Brushes.White;

    public DashboardViewModel(AppServices services, Action<Preset> openBoost)
    {
        _services = services;
        _ = openBoost;
        History = [];
        QuickBoostCommand = new AsyncRelayCommand(QuickBoost, () => !IsBusy);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        services.State.Changed += Reload;
        Reload();
        Sample();
    }

    public ObservableCollection<string> History { get; }
    public AsyncRelayCommand QuickBoostCommand { get; }
    public double Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public double Ram { get => _ram; private set => Set(ref _ram, value); }
    public double Gpu { get => _gpu; private set => Set(ref _gpu, value); }
    public double Disk { get => _disk; private set => Set(ref _disk, value); }
    public double Score { get => _score; private set => Set(ref _score, value); }
    public double BoostProgress { get => _boostProgress; private set { if (Set(ref _boostProgress, value)) Raise(nameof(BoostRing)); } }
    public double BoostRing => IsBusy ? BoostProgress : 100;
    public string CpuLabel { get => _cpuLabel; private set => Set(ref _cpuLabel, value); }
    public string RamLabel { get => _ramLabel; private set => Set(ref _ramLabel, value); }
    public string GpuLabel { get => _gpuLabel; private set => Set(ref _gpuLabel, value); }
    public string DiskLabel { get => _diskLabel; private set => Set(ref _diskLabel, value); }
    public string ScoreLabel { get => _scoreLabel; private set => Set(ref _scoreLabel, value); }
    public string CpuDetail { get => _cpuDetail; private set => Set(ref _cpuDetail, value); }
    public string RamDetail { get => _ramDetail; private set => Set(ref _ramDetail, value); }
    public string GpuDetail { get => _gpuDetail; private set => Set(ref _gpuDetail, value); }
    public string DiskDetail { get => _diskDetail; private set => Set(ref _diskDetail, value); }
    public string ScoreDetail { get => _scoreDetail; private set => Set(ref _scoreDetail, value); }
    public string GameModeText { get => _gameModeText; private set => Set(ref _gameModeText, value); }
    public string PowerPlanText { get => _powerPlanText; private set => Set(ref _powerPlanText, value); }
    public string PingText { get => _pingText; private set => Set(ref _pingText, value); }
    public string BoostStatus { get => _boostStatus; private set => Set(ref _boostStatus, value); }
    public string BoostTitle => IsBusy ? BoostProgress.ToString("0", _culture) + " %" : "BOOST";
    public IReadOnlyList<double> CpuSamples => _cpuView;
    public IReadOnlyList<double> RamSamples => _ramView;
    public Brush CpuBrush { get => _cpuBrush; private set => Set(ref _cpuBrush, value); }
    public Brush RamBrush { get => _ramBrush; private set => Set(ref _ramBrush, value); }
    public Brush GpuBrush { get => _gpuBrush; private set => Set(ref _gpuBrush, value); }
    public Brush DiskBrush { get => _diskBrush; private set => Set(ref _diskBrush, value); }
    public Brush ScoreBrush { get => _scoreBrush; private set => Set(ref _scoreBrush, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value))
                return;
            Raise(nameof(BoostRing));
            Raise(nameof(BoostTitle));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ActivePresetName { get; private set; } = "";

    public void Reload()
    {
        History.Clear();
        foreach (var entry in _services.State.Profile.History.Take(6))
        {
            var mark = entry.Success ? "OK" : "Échec";
            History.Add(TimeStamp.Local(entry.OccurredAt) + "  " + mark + "  " + entry.Detail);
        }
        if (History.Count == 0)
            History.Add("Aucun boost enregistré pour ce profil.");
        var id = _services.State.Profile.Settings.Preferences.ActivePresetId;
        ActivePresetName = Resolve(id)?.Name ?? "Compétitif / Fortnite";
        Raise(nameof(ActivePresetName));
        if (!IsBusy)
            BoostStatus = ActivePresetName;
    }

    private async Task QuickBoost()
    {
        var preset = Resolve(_services.State.Profile.Settings.Preferences.ActivePresetId) ?? PresetCatalog.BuiltIns[0];
        if (!Dialogs.Confirm("Lancer « " + preset.Name + " » maintenant ? Les fichiers supprimés ne seront pas restaurés."))
            return;
        IsBusy = true;
        BoostProgress = 0;
        BoostStatus = "Préparation";
        try
        {
            var progress = new Progress<BoostProgress>(step =>
            {
                BoostProgress = step.Percent;
                BoostStatus = step.Label;
                Raise(nameof(BoostTitle));
            });
            var apps = _services.State.Profile.Settings.Preferences.PrecheckBackgroundApps.ToArray();
            await _services.Tweaks.ApplyAsync(preset.Tweaks.Clone(), apps, preset.Id, preset.Name, progress: progress);
            Reload();
            Sample();
        }
        finally
        {
            IsBusy = false;
            BoostProgress = 100;
            BoostStatus = ActivePresetName;
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
        Cpu = snap.Cpu;
        Ram = snap.RamPercent;
        Gpu = snap.Gpu ?? 0;
        Disk = snap.DiskPercent;
        CpuLabel = snap.Cpu.ToString("0", _culture) + " %";
        RamLabel = snap.RamPercent.ToString("0", _culture) + " %";
        GpuLabel = snap.Gpu == null ? "—" : snap.Gpu.Value.ToString("0", _culture) + " %";
        DiskLabel = snap.DiskPercent.ToString("0", _culture) + " %";
        CpuDetail = snap.CpuTemp == null ? "" : snap.CpuTemp.Value.ToString("0", _culture) + " °C";
        RamDetail = snap.RamTotalGb <= 0
            ? ""
            : snap.RamUsedGb.ToString("0.0", _culture) + " / " + snap.RamTotalGb.ToString("0.0", _culture) + " Go";
        GpuDetail = DescribeGpu(snap);
        DiskDetail = DescribeDisk(snap);
        CpuBrush = BrushFor(snap.Cpu, "Brush.Lime");
        RamBrush = BrushFor(snap.RamPercent, "Brush.Teal");
        GpuBrush = snap.Gpu == null ? Brush("Brush.Faint") : BrushFor(snap.Gpu.Value, "Brush.Violet");
        DiskBrush = BrushFor(snap.DiskPercent, "Brush.Sky");

        var boosted = IsBoosted();
        var headroom = (100 - snap.Cpu) * 0.40
            + (100 - snap.RamPercent) * 0.25
            + (100 - (snap.Gpu ?? 30)) * 0.20
            + (100 - snap.DiskPercent) * 0.15;
        var score = Math.Clamp(headroom * 0.64 + (boosted ? 36 : 0), 0, 100);
        Score = score;
        ScoreLabel = score.ToString("0", _culture);
        ScoreDetail = boosted ? "Boost appliqué" : "Avant le boost";
        ScoreBrush = BrushFor(100 - score, "Brush.Lime");

        Push(_cpuSamples, snap.Cpu);
        Push(_ramSamples, snap.RamPercent);
        _cpuView = _cpuSamples.ToArray();
        _ramView = _ramSamples.ToArray();
        Raise(nameof(CpuSamples));
        Raise(nameof(RamSamples));

        var tick = _tick++;
        if (tick % 5 == 0)
            _ = RefreshStatusAsync();
        if (tick % 8 == 0)
            _ = RefreshPingAsync();
    }

    private bool IsBoosted()
    {
        if (_services.State.BoostApplied)
            return true;
        foreach (var entry in _services.State.Profile.History)
        {
            if (!entry.Success)
                continue;
            if (entry.Action is "boost" or "preset")
                return true;
            if (entry.Action == "restore")
                return false;
        }
        return false;
    }

    private async Task RefreshStatusAsync()
    {
        if (Interlocked.Exchange(ref _statusBusy, 1) == 1)
            return;
        try
        {
            var power = await Task.Run(() =>
            {
                try { return _services.Power.FrenchName(_services.Power.GetActiveScheme()); }
                catch { return "Indisponible"; }
            });
            PowerPlanText = power;
            GameModeText = ReadGameMode();
        }
        finally
        {
            Interlocked.Exchange(ref _statusBusy, 0);
        }
    }

    private async Task RefreshPingAsync()
    {
        if (Interlocked.Exchange(ref _pingBusy, 1) == 1)
            return;
        try
        {
            var host = _services.State.Profile.Settings.Preferences.PingHost;
            if (!PreferenceRules.IsHost(host))
            {
                PingText = "—";
                return;
            }
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(800));
            PingText = reply.Status == IPStatus.Success ? reply.RoundtripTime.ToString(_culture) + " ms" : "—";
        }
        catch
        {
            PingText = "—";
        }
        finally
        {
            Interlocked.Exchange(ref _pingBusy, 0);
        }
    }

    private static string ReadGameMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            var value = key?.GetValue("AutoGameModeEnabled");
            if (value == null)
                return "Inconnu";
            var enabled = value switch
            {
                int i => i != 0,
                long l => l != 0,
                string s => s is "1" or "true",
                _ => false
            };
            return enabled ? "Activé" : "Désactivé";
        }
        catch
        {
            return "Inconnu";
        }
    }

    private string DescribeGpu(MetricSnapshot snap)
    {
        var parts = new List<string>();
        if (snap.VramTotalGb is > 0)
            parts.Add(snap.VramUsedGb.GetValueOrDefault().ToString("0.0", _culture) + " / " + snap.VramTotalGb.Value.ToString("0.0", _culture) + " Go");
        if (snap.GpuTemp != null)
            parts.Add(snap.GpuTemp.Value.ToString("0", _culture) + " °C");
        if (parts.Count == 0)
            return snap.Gpu == null ? "Non détecté" : "";
        return string.Join(" · ", parts);
    }

    private string DescribeDisk(MetricSnapshot snap)
    {
        var kind = snap.DiskIsActivity ? "activité" : "espace";
        if (snap.DiskTotalGb <= 0)
            return kind;
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        var letter = string.IsNullOrWhiteSpace(root) ? "" : root.TrimEnd('\\') + " ";
        return kind + " · " + letter + snap.DiskUsedGb.ToString("0", _culture) + " / " + snap.DiskTotalGb.ToString("0", _culture) + " Go";
    }

    private static void Push(List<double> samples, double value)
    {
        samples.Add(value);
        if (samples.Count > 60)
            samples.RemoveAt(0);
    }

    private Brush BrushFor(double value, string normalKey)
    {
        if (value >= 90)
            return Brush("Brush.Coral");
        if (value >= 75)
            return Brush("Brush.Warn");
        return Brush(normalKey);
    }

    private static Brush Brush(string key)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return Brushes.White;
    }
}
