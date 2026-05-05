using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Agents;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class AgentsController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<CreateAgentRequest, AgentResponse>(
                "/agents", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid agent request." });
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
            var result = await api.GetAsync<IReadOnlyList<AgentResponse>>("/agents", ct);
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
            var result = await api.GetAsync<AgentResponse>($"/agents/{id}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Agent not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{id:guid}/cost")]
    public async Task<IActionResult> GetCost(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<AgentCostResponse>($"/agents/{id}/cost", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Agent not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<UpdateAgentRequest, AgentResponse>(
                $"/agents/{id}", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Agent not found." });
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
            var success = await api.DeleteAsync($"/agents/{id}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> AssignRole(
        Guid id, AssignAgentRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<AssignAgentRoleRequest, AgentResponse>(
                $"/agents/{id}/role", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Agent not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Insufficient permissions." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("sync-with-models")]
    public async Task<IActionResult> SyncWithModels(CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<object, IReadOnlyList<AgentResponse>>(
                "/agents/sync-with-models", new { }, ct);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
