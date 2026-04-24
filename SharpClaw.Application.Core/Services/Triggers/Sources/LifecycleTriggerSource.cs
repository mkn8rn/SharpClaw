using SharpClaw.Application.Infrastructure.Tasks;
using Microsoft.Extensions.Hosting;
using SharpClaw.Application.Infrastructure.Tasks;
using Microsoft.Extensions.Logging;
using SharpClaw.Application.Infrastructure.Tasks;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Application.Core.Services.Triggers.Sources;

/// <summary>
/// Fires once on application startup (<see cref="TriggerKind.Startup"/>) and
/// registers a shutdown callback (<see cref="TriggerKind.Shutdown"/>) via
/// <see cref="IHostApplicationLifetime"/>.
/// </summary>
public sealed class LifecycleTriggerSource(
    IHostApplicationLifetime lifetime,
    ILogger<LifecycleTriggerSource> logger) : ITaskTriggerSource
{
    private IReadOnlyList<ITaskTriggerSourceContext> _contexts = [];
    private CancellationTokenRegistration _startReg;
    private CancellationTokenRegistration _stopReg;

    public IReadOnlyList<TriggerKind> SupportedKinds { get; } =
        [TriggerKind.Startup, TriggerKind.Shutdown];

    public Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct)
    {
        _contexts = contexts;

        _startReg = lifetime.ApplicationStarted.Register(() =>
            _ = Task.Run(() => FireMatchingAsync(TriggerKind.Startup)));

        _stopReg = lifetime.ApplicationStopping.Register(() =>
            _ = Task.Run(() => FireMatchingAsync(TriggerKind.Shutdown)));

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _startReg.Dispose();
        _stopReg.Dispose();
        _contexts = [];
        return Task.CompletedTask;
    }

    private async Task FireMatchingAsync(TriggerKind kind)
    {
        foreach (var ctx in _contexts.Where(c => c.Definition.Kind == kind))
        {
            try { await ctx.FireAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "LifecycleTriggerSource failed to fire {Kind} context for definition {Id}.",
                    kind, ctx.TaskDefinitionId);
            }
        }
    }
}
