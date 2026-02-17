using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace SharpClaw.Application.API.Api;

public sealed class ApiKeyMiddleware(RequestDelegate next, ApiKeyProvider keyProvider)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(providedKey.ToString()),
                System.Text.Encoding.UTF8.GetBytes(keyProvider.ApiKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid or missing API key.");
            return;
        }

        await next(context);
    }
}
