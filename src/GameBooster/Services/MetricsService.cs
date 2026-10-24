using System.Diagnostics;
using System.Globalization;

namespace GameBooster.Services;

public sealed class MetricSnapshot
{
    public double Cpu { get; init; }
    public double? CpuTemp { get; init; }
    public double RamPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double? Gpu { get; init; }
    public double? GpuTemp { get; init; }
    public double? VramUsedGb { get; init; }
    public double? VramTotalGb { get; init; }
    public double DiskPercent { get; init; }
    public bool DiskIsActivity { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskTotalGb { get; init; }
}

public sealed class MetricsService : IDisposable
{
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private readonly List<PerformanceCounter> _gpu = [];
    private readonly List<PerformanceCounter> _zones = [];
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _diskIdle;
    private int _tick;
    private int _sampling;
    private MetricSnapshot _latest = new();
    private string? _nvidia;
    private double? _gpuTemp;
    private double? _vramUsed;
    private double? _vramTotal;
    private double? _nvidiaUtil;
    private double _diskUsed;
    private double _diskTotal;
    private bool _diskCounterOk;

    public MetricsService()
    {
        try
        {
            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _cpu.NextValue();
        }
        catch
        {
            _cpu = null;
        }
        try
        {
            _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
            _diskIdle.NextValue();
            _diskCounterOk = true;
        }
        catch
        {
            _diskIdle = null;
            _diskCounterOk = false;
        }
        TryOpenZones();
        _nvidia = FindNvidiaSmi();
        _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public MetricSnapshot Latest
    {
        get { lock (_gate) return _latest; }
    }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1)
            return;
        try
        {
            var tick = _tick++;
            double cpu = 0;
            try { cpu = Math.Clamp(_cpu?.NextValue() ?? 0, 0, 100); } catch { cpu = 0; }

            double ramPct = 0, used = 0, total = 0;
            try
            {
                var memory = new NativeMethods.MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
                if (NativeMethods.GlobalMemoryStatusEx(ref memory) && memory.TotalPhys > 0)
                {
                    ramPct = memory.MemoryLoad;
                    total = memory.TotalPhys / 1024d / 1024d / 1024d;
                    used = (memory.TotalPhys - memory.AvailPhys) / 1024d / 1024d / 1024d;
                }
            }
            catch
            {
                ramPct = 0;
            }

            if (tick % 8 == 0)
                RebuildGpu();
            var gpu = ReadGpu();
            if (tick % 8 == 4)
                ReadNvidia();
            if (gpu == null)
                gpu = _nvidiaUtil;
            if (tick % 5 == 0)
                ReadDiskSpace();

            double diskPct = 0;
            var diskActivity = false;
            if (_diskCounterOk && _diskIdle != null)
            {
                try
                {
                    diskPct = Math.Clamp(100d - _diskIdle.NextValue(), 0, 100);
                    diskActivity = true;
                }
                catch
                {
                    _diskCounterOk = false;
                }
            }
            if (!diskActivity && _diskTotal > 0)
                diskPct = Math.Clamp(_diskUsed / _diskTotal * 100d, 0, 100);

            lock (_gate)
            {
                _latest = new MetricSnapshot
                {
                    Cpu = cpu,
                    CpuTemp = ReadZoneTemp(),
                    RamPercent = ramPct,
                    RamUsedGb = used,
                    RamTotalGb = total,
                    Gpu = gpu,
                    GpuTemp = _gpuTemp,
                    VramUsedGb = _vramUsed,
                    VramTotalGb = _vramTotal,
                    DiskPercent = diskPct,
                    DiskIsActivity = diskActivity,
                    DiskUsedGb = _diskUsed,
                    DiskTotalGb = _diskTotal
                };
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    private double? ReadGpu()
    {
        try
        {
            if (_gpu.Count == 0)
                return null;
            double sum = 0;
            foreach (var counter in _gpu)
            {
                try { sum += counter.NextValue(); } catch { /* instance disparue */ }
            }
            return Math.Clamp(sum, 0, 100);
        }
        catch
        {
            return null;
        }
    }

    private void RebuildGpu()
    {
        foreach (var counter in _gpu)
            counter.Dispose();
        _gpu.Clear();
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;
                _gpu.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true));
            }
        }
        catch
        {
            _gpu.Clear();
        }
    }

    private void ReadNvidia()
    {
        if (_nvidia == null)
            return;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _nvidia,
                Arguments = "--query-gpu=temperature.gpu,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(info);
            if (process == null)
                return;
            if (!process.WaitForExit(1200))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
                return;
            }
            var first = process.StandardOutput.ReadToEnd()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(first))
                return;
            var parts = first.Split(',');
            if (parts.Length < 4)
                return;
            _gpuTemp = ParseOrNull(parts[0]);
            var usedMiB = ParseOrNull(parts[1]);
            var totalMiB = ParseOrNull(parts[2]);
            _vramUsed = usedMiB == null ? null : usedMiB.Value / 1024d;
            _vramTotal = totalMiB == null ? null : totalMiB.Value / 1024d;
            _nvidiaUtil = ParseOrNull(parts[3]);
            if (_nvidiaUtil != null)
                _nvidiaUtil = Math.Clamp(_nvidiaUtil.Value, 0, 100);
        }
        catch
        {
            // nvidia-smi absent ou occupé
        }
    }

    private void ReadDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
                return;
            _diskTotal = drive.TotalSize / 1024d / 1024d / 1024d;
            _diskUsed = (drive.TotalSize - drive.AvailableFreeSpace) / 1024d / 1024d / 1024d;
        }
        catch
        {
            // lecteur indisponible
        }
    }

    private void TryOpenZones()
    {
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            foreach (var instance in category.GetInstanceNames())
            {
                try
                {
                    var counter = new PerformanceCounter("Thermal Zone Information", "Temperature", instance, readOnly: true);
                    counter.NextValue();
                    _zones.Add(counter);
                }
                catch
                {
                    // zone illisible
                }
            }
        }
        catch
        {
            _zones.Clear();
        }
    }

    private double? ReadZoneTemp()
    {
        double? best = null;
        foreach (var counter in _zones)
        {
            try
            {
                var raw = counter.NextValue();
                var celsius = raw > 200 ? raw / 10d - 273.15 : raw;
                if (celsius < 5 || celsius > 120)
                    continue;
                if (best == null || celsius > best)
                    best = celsius;
            }
            catch
            {
                // compteur disparu
            }
        }
        return best;
    }

    private static double? ParseOrNull(string token)
    {
        var text = token.Trim();
        if (text.Length == 0 || text.Contains("N/A", StringComparison.OrdinalIgnoreCase))
            return null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? FindNvidiaSmi()
    {
        var bundled = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        if (File.Exists(bundled))
            return bundled;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path == null)
            return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "nvidia-smi.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // entrée PATH invalide
            }
        }
        return null;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cpu?.Dispose();
        _diskIdle?.Dispose();
        foreach (var counter in _gpu)
            counter.Dispose();
        foreach (var counter in _zones)
            counter.Dispose();
    }
}
