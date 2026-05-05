using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Channels;
using SharpClaw.Contracts.DTOs.DefaultResources;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class ChannelsController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChannelRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<CreateChannelRequest, ChannelResponse>(
                "/channels", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid channel request." });
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
                ? $"/channels?agentId={agentId}"
                : "/channels";
            var result = await api.GetAsync<IReadOnlyList<ChannelResponse>>(path, ct);
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
            var result = await api.GetAsync<ChannelResponse>($"/channels/{id}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateChannelRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<UpdateChannelRequest, ChannelResponse>(
                $"/channels/{id}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
            var success = await api.DeleteAsync($"/channels/{id}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    // ── Default agent ─────────────────────────────────────────────

    [HttpPut("{id:guid}/agent")]
    public async Task<IActionResult> SetAgent(
        Guid id, SetChannelAgentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<SetChannelAgentRequest, ChannelResponse>(
                $"/channels/{id}/agent", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
            var result = await api.GetAsync<ChannelAllowedAgentsResponse>(
                $"/channels/{id}/agents", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("{id:guid}/agents")]
    public async Task<IActionResult> AddAllowedAgent(
        Guid id, AddAllowedAgentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<AddAllowedAgentRequest, ChannelAllowedAgentsResponse>(
                $"/channels/{id}/agents", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
            var success = await api.DeleteAsync($"/channels/{id}/agents/{agentId}", ct);
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
                $"/channels/{id}/defaults", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
                $"/channels/{id}/defaults", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
                $"/channels/{id}/defaults/{key}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Channel not found." });
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
            var success = await api.DeleteAsync($"/channels/{id}/defaults/{key}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
