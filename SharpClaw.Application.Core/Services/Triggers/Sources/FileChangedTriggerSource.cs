using SharpClaw.Application.Infrastructure.Tasks;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Application.Core.Services.Triggers.Sources;

/// <summary>
/// Trigger source that fires when files under a watched path are created,
/// changed, deleted, or renamed via <see cref="FileSystemWatcher"/>.
/// </summary>
public sealed class FileChangedTriggerSource(
    ILogger<FileChangedTriggerSource> logger) : ITaskTriggerSource, IAsyncDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private IReadOnlyList<ITaskTriggerSourceContext> _contexts = [];

    public IReadOnlyList<TriggerKind> SupportedKinds { get; } = [TriggerKind.FileChanged];

    public Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct)
    {
        StopWatchers();
        _contexts = contexts;

        foreach (var ctx in contexts)
        {
            var path = ctx.Definition.WatchPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                logger.LogWarning(
                    "FileChangedTriggerSource: definition {Id} has no WatchPath; skipping.",
                    ctx.TaskDefinitionId);
                continue;
            }

            var dir     = Path.GetDirectoryName(path) ?? path;
            var pattern = ctx.Definition.FilePattern ?? "*";

            if (!Directory.Exists(dir))
            {
                logger.LogWarning(
                    "FileChangedTriggerSource: watch directory '{Dir}' does not exist; skipping.", dir);
                continue;
            }

            var watcher = new FileSystemWatcher(dir, pattern)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents   = false,
            };

            var events = ctx.Definition.FileEvents == 0 ? FileWatchEvent.Any : ctx.Definition.FileEvents;

            if (events.HasFlag(FileWatchEvent.Created)) watcher.Created += (_, _) => Fire(ctx);
            if (events.HasFlag(FileWatchEvent.Changed)) watcher.Changed += (_, _) => Fire(ctx);
            if (events.HasFlag(FileWatchEvent.Deleted)) watcher.Deleted += (_, _) => Fire(ctx);
            if (events.HasFlag(FileWatchEvent.Renamed)) watcher.Renamed += (_, _) => Fire(ctx);

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopWatchers();
        _contexts = [];
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        StopWatchers();
        return ValueTask.CompletedTask;
    }

    private void StopWatchers()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FileSystemWatcher dispose failed.");
            }
        }

        _watchers.Clear();
    }

    private void Fire(ITaskTriggerSourceContext ctx) =>
        _ = Task.Run(async () =>
        {
            try { await ctx.FireAsync(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "FileChangedTriggerSource failed to fire context for definition {Id}.",
                    ctx.TaskDefinitionId);
            }
        });
}
