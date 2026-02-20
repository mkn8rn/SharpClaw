using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.PublicAPI.Infrastructure;

namespace SharpClaw.PublicAPI.Controllers;

[ApiController]
[Route("api/channels/{channelId:guid}/chat")]
[EnableRateLimiting(Security.RateLimiterConfiguration.ChatPolicy)]
public class ChatController(InternalApiClient api) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Send(Guid channelId, ChatRequest request, CancellationToken ct)
    {
        try
        {
            var result = await api.PostAsync<ChatRequest, ChatResponse>(
                $"/channels/{channelId}/chat", request, ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Conversation not found." });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return BadRequest(new { error = "Invalid chat request." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(Guid channelId, CancellationToken ct)
    {
        try
        {
            var result = await api.GetAsync<IReadOnlyList<ChatMessageResponse>>(
                $"/channels/{channelId}/chat", ct);
            return Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound(new { error = "Conversation not found." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Internal service unavailable." });
        }
    }
}
