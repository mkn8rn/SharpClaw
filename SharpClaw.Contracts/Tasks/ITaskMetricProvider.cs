namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Provides a named metric value that can be polled by a metric trigger source
/// for threshold comparisons.
/// </summary>
public interface ITaskMetricProvider
{
    /// <summary>Unique name used in <c>[OnMetricThreshold("MetricName", …)]</c>.</summary>
    string MetricName { get; }

    /// <summary>Human-readable description of the metric.</summary>
    string Description { get; }

    /// <summary>Returns the current value of this metric.</summary>
    Task<double> GetValueAsync(CancellationToken ct);
}
