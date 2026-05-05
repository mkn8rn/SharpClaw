using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Roles;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class RolesController(InternalApiClient api) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<IReadOnlyList<RoleResponse>>("/roles", ct);
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
            var result = await api.GetAsync<RoleResponse>($"/roles/{id}", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Role not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("{id:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<RolePermissionsResponse>($"/roles/{id}/permissions", ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Role not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<CreateRoleRequest, RoleResponse>(
                "/roles", request, ct);
            return result is not null ? Created($"/api/roles/{result.Id}", result) : BadRequest();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid role request." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}/name")]
    public async Task<IActionResult> Rename(Guid id, RenameRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<RenameRoleRequest, RoleResponse>(
                $"/roles/{id}/name", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Role not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Conflict(new { error = "Role name conflict." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(
        Guid id, SetRolePermissionsRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<SetRolePermissionsRequest, RolePermissionsResponse>(
                $"/roles/{id}/permissions", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Role not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Insufficient permissions." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return Conflict(new { error = "Permission conflict." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Not authenticated." });
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
            var success = await api.DeleteAsync($"/roles/{id}", ct);
            return success ? NoContent() : NotFound();
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
