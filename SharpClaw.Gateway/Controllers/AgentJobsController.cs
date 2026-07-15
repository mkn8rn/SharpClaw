using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.DTOs.Agents;
using SharpClaw.Contracts.DTOs.Diagnostics;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/channels/{channelId:guid}/jobs")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class AgentJobsController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit(
        Guid channelId, SubmitAgentJobRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<SubmitAgentJobRequest, AgentJobResponse>(
                $"/channels/{channelId}/jobs", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid job request." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        Guid channelId,
        string? cursor,
        int take = 50,
        CancellationToken ct = default)
    {
        try
        {
            var path = $"/channels/{channelId}/jobs?take={take}";
            if (!string.IsNullOrWhiteSpace(cursor))
                path += $"&cursor={Uri.EscapeDataString(cursor)}";
            var result = await api.GetAsync<AgentJobSummaryPageResponse>(
                path,
                ct);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> GetById(Guid channelId, Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<AgentJobDetailResponse>(
                $"/channels/{channelId}/jobs/{jobId}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{jobId:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid channelId, Guid jobId, ApproveAgentJobRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<ApproveAgentJobRequest, AgentJobResponse>(
                $"/channels/{channelId}/jobs/{jobId}/approve", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{jobId:guid}/stop")]
    public async Task<IActionResult> Stop(Guid channelId, Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<object, AgentJobDetailResponse>(
                $"/channels/{channelId}/jobs/{jobId}/stop", new { }, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid channelId, Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<object, AgentJobDetailResponse>(
                $"/channels/{channelId}/jobs/{jobId}/cancel", new { }, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{jobId:guid}/pause")]
    public async Task<IActionResult> Pause(Guid channelId, Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<object, AgentJobDetailResponse>(
                $"/channels/{channelId}/jobs/{jobId}/pause", new { }, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{jobId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid channelId, Guid jobId, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<object, AgentJobDetailResponse>(
                $"/channels/{channelId}/jobs/{jobId}/resume", new { }, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{jobId:guid}/logs")]
    public async Task<IActionResult> GetLogs(
        Guid channelId,
        Guid jobId,
        string? cursor,
        int take = 200,
        int maxBytes = 262_144,
        string? minimumLevel = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? contains = null,
        long maxScanBytes = 16 * 1024 * 1024,
        CancellationToken ct = default)
    {
        try
        {
            var values = new Dictionary<string, string?>
            {
                ["cursor"] = cursor,
                ["take"] = take.ToString(),
                ["maxBytes"] = maxBytes.ToString(),
                ["minimumLevel"] = minimumLevel,
                ["from"] = from?.ToString("O"),
                ["to"] = to?.ToString("O"),
                ["contains"] = contains,
                ["maxScanBytes"] = maxScanBytes.ToString(),
            };
            var result = await api.GetAsync<DurableLogPageResponse>(
                AddQuery(
                    $"/channels/{channelId}/jobs/{jobId}/logs",
                    values),
                ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{jobId:guid}/audit")]
    public async Task<IActionResult> GetAudit(
        Guid channelId,
        Guid jobId,
        string? cursor,
        int take = 50,
        CancellationToken ct = default)
    {
        try
        {
            var values = new Dictionary<string, string?>
            {
                ["cursor"] = cursor,
                ["take"] = take.ToString(),
            };
            var result = await api.GetAsync<ExecutionAuditPageResponse>(
                AddQuery(
                    $"/channels/{channelId}/jobs/{jobId}/audit",
                    values),
                ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Job not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{jobId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> GetArtifact(
        Guid channelId,
        Guid jobId,
        Guid artifactId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/channels/{channelId}/jobs/{jobId}/artifacts/{artifactId}");
        if (Request.Headers.Range.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Range",
                Request.Headers.Range.ToArray());
        }

        using var response = await api.SendRawAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return NotFound(new { error = "Job artifact not found." });
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Internal service unavailable." });
        }

        Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Headers)
        {
            if (!string.Equals(
                    header.Key,
                    "Transfer-Encoding",
                    StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        await response.Content.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }

    private static string AddQuery(
        string path,
        IReadOnlyDictionary<string, string?> values)
    {
        var query = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}="
                + Uri.EscapeDataString(pair.Value!));
        return path + "?" + string.Join("&", query);
    }
}
