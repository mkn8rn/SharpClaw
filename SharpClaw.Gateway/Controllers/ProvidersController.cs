using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Providers;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class ProvidersController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateProviderRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<CreateProviderRequest, ProviderResponse>(
                "/providers", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid provider request." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<IReadOnlyList<ProviderResponse>>("/providers", ct);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<ProviderResponse>($"/providers/{id}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateProviderRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<UpdateProviderRequest, ProviderResponse>(
                $"/providers/{id}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            var success = await api.DeleteAsync($"/providers/{id}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/sync-models")]
    public async Task<IActionResult> SyncModels(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<object, IReadOnlyList<ProviderResponse>>(
                $"/providers/{id}/sync-models", new { }, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/set-key")]
    public async Task<IActionResult> SetApiKey(Guid id, SetApiKeyRequest request, CancellationToken ct)
    {
        try
        {
            await api.PostAsync($"/providers/{id}/set-key", request, ct);
            return NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/auth/device-code")]
    public async Task<IActionResult> StartDeviceCodeFlow(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<object, DeviceCodeResponse>(
                $"/providers/{id}/auth/device-code", new { }, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/auth/device-code/poll")]
    public async Task<IActionResult> PollDeviceCodeFlow(
        Guid id, DeviceCodePollRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<DeviceCodePollRequest, object>(
                $"/providers/{id}/auth/device-code/poll", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { status = "expired" });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{id:guid}/cost")]
    public async Task<IActionResult> GetCost(
        Guid id,
        [FromQuery] int? days,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        CancellationToken ct)
    {
        try
        {
            var path = BuildCostQueryString($"/providers/{id}/cost", days, startDate, endDate);
            var result = await api.GetAsync<ProviderCostResponse>(path, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Provider not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("cost/total")]
    public async Task<IActionResult> GetCostTotal(
        [FromQuery] int? days,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] bool? all,
        [FromQuery] bool? simple,
        CancellationToken ct)
    {
        try
        {
            var path = BuildCostQueryString("/providers/cost/total", days, startDate, endDate);

            if (all is true)
                path += (path.Contains('?') ? "&" : "?") + "all=true";
            if (simple is true)
                path += (path.Contains('?') ? "&" : "?") + "simple=true";

            // Simple mode returns a different shape — use JsonElement to forward as-is
            if (simple is true)
            {
                var result = await api.GetAsync<ProviderCostSimpleResponse>(path, ct);
                return Ok(result);
            }
            else
            {
                var result = await api.GetAsync<ProviderCostTotalResponse>(path, ct);
                return Ok(result);
            }
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    private static string BuildCostQueryString(
        string basePath, int? days, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        var parts = new List<string>();

        if (days.HasValue)
            parts.Add($"days={days.Value}");
        if (startDate.HasValue)
            parts.Add($"startDate={startDate.Value:O}");
        if (endDate.HasValue)
            parts.Add($"endDate={endDate.Value:O}");

        return parts.Count > 0 ? $"{basePath}?{string.Join('&', parts)}" : basePath;
    }
}
