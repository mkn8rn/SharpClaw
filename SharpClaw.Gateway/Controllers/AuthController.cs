using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Agents;
using SharpClaw.Contracts.DTOs.Auth;
using SharpClaw.Gateway.Infrastructure;

namespace SharpClaw.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.AuthPolicy)]
public class AuthController(InternalApiClient api) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<RegisterRequest, object>("/auth/register", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<LoginRequest, LoginResponse>("/auth/login", request, ct);
            return result is not null ? Ok(result) : Unauthorized();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Invalid credentials." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<RefreshRequest, LoginResponse>("/auth/refresh", request, ct);
            return result is not null ? Ok(result) : Unauthorized();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<MeResponse>("/auth/me", ct);
            return result is not null ? Ok(result) : Unauthorized();
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

    [HttpPut("me/role")]
    public async Task<IActionResult> AssignSelfRole(AssignAgentRoleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PutAsync<AssignAgentRoleRequest, MeResponse>("/auth/me/role", request, ct);
            return result is not null ? Ok(result) : NotFound();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Not authenticated." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
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

    [HttpPost("invalidate-access-tokens")]
    public async Task<IActionResult> InvalidateAccessTokens(InvalidateRequest request, CancellationToken ct)
    {
        try
        {
            await api.PostAsync<InvalidateRequest>("/auth/invalidate-access-tokens", request, ct);
            return NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Not authenticated." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpPost("invalidate-refresh-tokens")]
    public async Task<IActionResult> InvalidateRefreshTokens(InvalidateRequest request, CancellationToken ct)
    {
        try
        {
            await api.PostAsync<InvalidateRequest>("/auth/invalidate-refresh-tokens", request, ct);
            return NoContent();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized(new { error = "Not authenticated." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
