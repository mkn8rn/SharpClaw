using Microsoft.AspNetCore.Http;
using SharpClaw.Runtime.BLL.Services;
using SharpClaw.Runtime.Host.Routing;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Shared.Logging;

namespace SharpClaw.Runtime.Host.Handlers;

[RouteGroup("/diagnostics")]
public static class DiagnosticHandlers
{
    [MapGet("/storage")]
    public static IResult GetStorageHealth(
        DurableStorageMaintenanceService maintenance) =>
        Results.Ok(maintenance.GetHealth());

    [MapGet("/process-logs")]
    public static async Task<IResult> GetProcessLogs(
        DurableProcessLogWriter processLogs,
        ExecutionDiagnosticStore diagnostics,
        string? cursor = null,
        int take = 200,
        int maxBytes = 262_144,
        string? minimumLevel = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? contains = null,
        long maxScanBytes = 16 * 1024 * 1024,
        CancellationToken ct = default)
    {
        var page = await diagnostics.ReadProcessLogsAsync(
            processLogs.AppName,
            processLogs.BootId,
            cursor,
            new DurableLogQuery(
                take,
                maxBytes,
                minimumLevel,
                from,
                to,
                contains,
                maxScanBytes),
            ct);
        return Results.Ok(new
        {
            application = processLogs.AppName,
            bootId = processLogs.BootId,
            page,
        });
    }
}
