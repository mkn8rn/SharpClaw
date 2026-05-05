using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Users;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class UsersController(InternalApiClient api) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<IReadOnlyList<UserEntry>>("/users", ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access required." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> AssignRole(
        Guid id, SetUserRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<SetUserRoleRequest, UserEntry>(
                $"/users/{id}/role", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "User not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Admin access required." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid role assignment." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
