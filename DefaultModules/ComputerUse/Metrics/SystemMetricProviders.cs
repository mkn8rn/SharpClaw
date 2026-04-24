using System.Diagnostics;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.ComputerUse.Metrics;

/// <summary>
/// Reports approximate process memory usage in megabytes (<c>System.MemoryUsedMb</c>).
/// </summary>
public sealed class SystemMemoryMetricProvider : ITaskMetricProvider
{
    public string MetricName  => "System.MemoryUsedMb";
    public string Description => "Current process working-set memory in megabytes.";

    public Task<double> GetValueAsync(CancellationToken ct)
    {
        var mb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        return Task.FromResult(mb);
    }
}

/// <summary>
/// Reports approximate CPU usage of the current process (<c>System.CpuPercent</c>).
/// </summary>
public sealed class SystemCpuMetricProvider : ITaskMetricProvider
{
    private DateTimeOffset _lastSample = DateTimeOffset.UtcNow;
    private TimeSpan       _lastCpu    = Process.GetCurrentProcess().TotalProcessorTime;

    public string MetricName  => "System.CpuPercent";
    public string Description => "CPU usage of the host process as a percentage.";

    public Task<double> GetValueAsync(CancellationToken ct)
    {
        var proc    = Process.GetCurrentProcess();
        var now     = DateTimeOffset.UtcNow;
        var cpu     = proc.TotalProcessorTime;
        var elapsed = (now - _lastSample).TotalMilliseconds;
        var cpuUsed = (cpu - _lastCpu).TotalMilliseconds;

        _lastSample = now;
        _lastCpu    = cpu;

        if (elapsed <= 0) return Task.FromResult(0.0);
        var percent = cpuUsed / (elapsed * Environment.ProcessorCount) * 100.0;
        return Task.FromResult(Math.Clamp(percent, 0, 100));
    }
}
