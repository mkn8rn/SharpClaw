using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Contexts;
using SharpClaw.Contracts.DTOs.DefaultResources;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/channel-contexts")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class ChannelContextsController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateContextRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<CreateContextRequest, ContextResponse>(
                "/channel-contexts", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid context request." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? agentId, CancellationToken ct)
    {
        try
        {
            var path = agentId is not null
                ? $"/channel-contexts?agentId={agentId}"
                : "/channel-contexts";
            var result = await api.GetAsync<IReadOnlyList<ContextResponse>>(path, ct);
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
            var result = await api.GetAsync<ContextResponse>($"/channel-contexts/{id}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateContextRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<UpdateContextRequest, ContextResponse>(
                $"/channel-contexts/{id}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
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
            var success = await api.DeleteAsync($"/channel-contexts/{id}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    // ── Allowed agents ────────────────────────────────────────────

    [HttpGet("{id:guid}/agents")]
    public async Task<IActionResult> ListAllowedAgents(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<ContextAllowedAgentsResponse>(
                $"/channel-contexts/{id}/agents", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/agents")]
    public async Task<IActionResult> AddAllowedAgent(
        Guid id, AddContextAllowedAgentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<AddContextAllowedAgentRequest, ContextAllowedAgentsResponse>(
                $"/channel-contexts/{id}/agents", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpDelete("{id:guid}/agents/{agentId:guid}")]
    public async Task<IActionResult> RemoveAllowedAgent(
        Guid id, Guid agentId, CancellationToken ct)
    {
        try
        {
            var success = await api.DeleteAsync($"/channel-contexts/{id}/agents/{agentId}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    // ── Default resources (bulk) ──────────────────────────────────

    [HttpGet("{id:guid}/defaults")]
    public async Task<IActionResult> GetDefaults(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<DefaultResourcesResponse>(
                $"/channel-contexts/{id}/defaults", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}/defaults")]
    public async Task<IActionResult> SetDefaults(
        Guid id, SetDefaultResourcesRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<SetDefaultResourcesRequest, DefaultResourcesResponse>(
                $"/channel-contexts/{id}/defaults", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    // ── Default resources (per-key) ───────────────────────────────

    [HttpPut("{id:guid}/defaults/{key}")]
    public async Task<IActionResult> SetDefaultByKey(
        Guid id, string key, SetDefaultResourceByKeyRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<SetDefaultResourceByKeyRequest, DefaultResourcesResponse>(
                $"/channel-contexts/{id}/defaults/{key}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Context not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = $"Unknown default resource key: {key}" });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpDelete("{id:guid}/defaults/{key}")]
    public async Task<IActionResult> ClearDefaultByKey(Guid id, string key, CancellationToken ct)
    {
        try
        {
            var success = await api.DeleteAsync($"/channel-contexts/{id}/defaults/{key}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
