using System.Diagnostics;
using System.Globalization;

namespace GameBooster.Services;

public sealed class MetricSnapshot
{
    public double Cpu { get; init; }
    public double RamPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double? Gpu { get; init; }
    public double? GpuTemp { get; init; }
}

public sealed class MetricsService : IDisposable
{
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private PerformanceCounter? _cpu;
    private List<PerformanceCounter> _gpu = [];
    private int _gpuRefresh;
    private MetricSnapshot _latest = new();
    private string? _nvidia;

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
        _nvidia = FindNvidiaSmi();
        _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public MetricSnapshot Latest
    {
        get { lock (_gate) return _latest; }
    }

    private void Sample()
    {
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

        double? gpu = ReadGpu();
        double? temp = ReadTemp();
        lock (_gate)
        {
            _latest = new MetricSnapshot
            {
                Cpu = cpu,
                RamPercent = ramPct,
                RamUsedGb = used,
                RamTotalGb = total,
                Gpu = gpu,
                GpuTemp = temp
            };
        }
    }

    private double? ReadGpu()
    {
        try
        {
            if (_gpuRefresh++ % 8 == 0)
                RebuildGpu();
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
        _gpu = [];
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
            _gpu = [];
        }
    }

    private double? ReadTemp()
    {
        if (_nvidia == null)
            return null;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _nvidia,
                Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(info);
            if (process == null)
                return null;
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
                return null;
            }
            var output = process.StandardOutput.ReadToEnd();
            var first = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first != null && double.TryParse(first.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                return temp;
        }
        catch
        {
            return null;
        }
        return null;
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
        foreach (var counter in _gpu)
            counter.Dispose();
    }
}
