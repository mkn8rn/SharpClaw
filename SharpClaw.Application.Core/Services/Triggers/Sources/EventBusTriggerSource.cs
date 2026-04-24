using SharpClaw.Application.Infrastructure.Tasks;
using Microsoft.Extensions.Logging;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Application.Core.Services.Triggers.Sources;

/// <summary>
/// Trigger source that fires on SharpClaw host events
/// (<see cref="TriggerKind.Event"/>, <see cref="TriggerKind.TaskCompleted"/>,
/// <see cref="TriggerKind.TaskFailed"/>).
/// Implements <see cref="ISharpClawEventSink"/> to receive events from the
/// <see cref="ModuleEventDispatcher"/>.
/// </summary>
public sealed class EventBusTriggerSource(
    ModuleEventDispatcher dispatcher,
    ILogger<EventBusTriggerSource> logger) : ITaskTriggerSource, ISharpClawEventSink
{
    private IReadOnlyList<ITaskTriggerSourceContext> _contexts = [];

    // ── ITaskTriggerSource ────────────────────────────────────────

    public IReadOnlyList<TriggerKind> SupportedKinds { get; } =
        [TriggerKind.Event, TriggerKind.TaskCompleted, TriggerKind.TaskFailed];

    public Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct)
    {
        _contexts = contexts;
        dispatcher.InvalidateSinkCache();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _contexts = [];
        dispatcher.InvalidateSinkCache();
        return Task.CompletedTask;
    }

    // ── ISharpClawEventSink ───────────────────────────────────────

    public SharpClawEventType SubscribedEvents =>
        SharpClawEventType.All;

    public async Task OnEventAsync(SharpClawEvent evt, CancellationToken ct)
    {
        foreach (var ctx in _contexts)
        {
            if (!MatchesContext(ctx, evt)) continue;

            try
            {
                await ctx.FireAsync(ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "EventBusTriggerSource failed to fire context for definition {Id}.",
                    ctx.TaskDefinitionId);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static bool MatchesContext(ITaskTriggerSourceContext ctx, SharpClawEvent evt)
    {
        var def = ctx.Definition;
        switch (def.Kind)
        {
            case TriggerKind.TaskCompleted:
                if (!evt.Type.HasFlag(SharpClawEventType.JobCompleted)) return false;
                break;
            case TriggerKind.TaskFailed:
                if (!evt.Type.HasFlag(SharpClawEventType.JobFailed)) return false;
                break;
            case TriggerKind.Event:
                if (string.IsNullOrWhiteSpace(def.EventType)) return false;
                // EventType may be a comma-separated list of SharpClawEventType flag names
                if (!MatchesEventTypeFilter(def.EventType, evt.Type)) return false;
                // Optional filter on SourceId / Summary
                if (!string.IsNullOrWhiteSpace(def.EventFilter) &&
                    !MatchesEventFilter(def.EventFilter, evt))
                    return false;
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool MatchesEventTypeFilter(string eventTypeFilter, SharpClawEventType actual)
    {
        foreach (var part in eventTypeFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SharpClawEventType>(part, ignoreCase: true, out var parsed) &&
                actual.HasFlag(parsed))
                return true;
        }

        return false;
    }

    private static bool MatchesEventFilter(string filter, SharpClawEvent evt) =>
        (evt.SourceId is not null && evt.SourceId.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
        (evt.Summary  is not null && evt.Summary.Contains(filter,  StringComparison.OrdinalIgnoreCase));
}
