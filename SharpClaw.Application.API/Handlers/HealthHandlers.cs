using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Infrastructure.Persistence.JSON;

namespace SharpClaw.Application.API.Handlers;

[RouteGroup("/system")]
public static class HealthHandlers
{
    /// <summary>
    /// Returns the persistence health status. No authentication required — suitable for
    /// monitoring probes. Responds 200 (Healthy), 207 (Degraded), or 503 (Unhealthy).
    /// </summary>
    [MapGet("/health")]
    public static async Task<IResult> GetHealth(
        JsonPersistenceHealthCheck healthCheck,
        CancellationToken ct)
    {
        var result = await healthCheck.CheckAsync(ct);

        var body = new
        {
            status = result.Status.ToString(),
            checks = result.Entries.Select(e => new
            {
                name = e.Name,
                status = e.Status.ToString(),
                description = e.Description
            }).ToArray()
        };

        return result.Status switch
        {
            HealthStatus.Healthy => Results.Ok(body),
            HealthStatus.Degraded => Results.Json(body, statusCode: StatusCodes.Status207MultiStatus),
            _ => Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }
}
