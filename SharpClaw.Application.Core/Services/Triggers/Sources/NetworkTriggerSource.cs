using SharpClaw.Application.Infrastructure.Tasks;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Application.Core.Services.Triggers.Sources;

/// <summary>
/// Fires when network availability changes, matching the optional SSID and
/// <see cref="NetworkState"/> declared on the binding.
/// </summary>
public sealed class NetworkTriggerSource(
    ILogger<NetworkTriggerSource> logger) : ITaskTriggerSource, IDisposable
{
    private IReadOnlyList<ITaskTriggerSourceContext> _contexts = [];
    private bool _subscribed;

    public IReadOnlyList<TriggerKind> SupportedKinds { get; } = [TriggerKind.NetworkChanged];

    public Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct)
    {
        _contexts = contexts;

        if (!_subscribed)
        {
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
            _subscribed = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (_subscribed)
        {
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
            _subscribed = false;
        }

        _contexts = [];
        return Task.CompletedTask;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var connected = e.IsAvailable;
        _ = Task.Run(async () =>
        {
            foreach (var ctx in _contexts)
            {
                var requiredState = ctx.Definition.NetworkState;

                if (requiredState == NetworkState.Connected    && !connected) continue;
                if (requiredState == NetworkState.Disconnected && connected)  continue;

                // SSID filtering is platform-specific; skip if not checkable
                var ssid = ctx.Definition.NetworkSsid;
                if (!string.IsNullOrWhiteSpace(ssid) && !TryMatchSsid(ssid))
                    continue;

                try { await ctx.FireAsync(); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "NetworkTriggerSource failed to fire context for definition {Id}.", ctx.TaskDefinitionId);
                }
            }
        });
    }

    /// <summary>SSID matching is best-effort; returns true when the check cannot be performed.</summary>
    private static bool TryMatchSsid(string expectedSsid)
    {
        // Full SSID detection requires platform-specific APIs not available cross-platform.
        // Return true so the trigger is not silently suppressed on unsupported platforms.
        return true;
    }
}
