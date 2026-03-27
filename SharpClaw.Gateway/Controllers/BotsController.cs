using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SharpClaw.Gateway.Configuration;

namespace SharpClaw.Gateway.Controllers;

/// <summary>
/// Status, configuration read and write endpoints for bot integrations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting(Security.RateLimiterConfiguration.GlobalPolicy)]
public class BotsController(
    IOptions<TelegramBotOptions> telegramOptions,
    IOptions<DiscordBotOptions> discordOptions) : ControllerBase
{
    private static readonly string EnvFilePath = Path.Combine(
        Path.GetDirectoryName(typeof(GatewayEnvironment).Assembly.Location)!,
        "Environment", ".env");

    [HttpGet("status")]
    public IActionResult Status()
    {
        var telegram = telegramOptions.Value;
        var discord = discordOptions.Value;

        return Ok(new
        {
            telegram = new
            {
                enabled = telegram.Enabled,
                configured = !string.IsNullOrWhiteSpace(telegram.BotToken)
            },
            discord = new
            {
                enabled = discord.Enabled,
                configured = !string.IsNullOrWhiteSpace(discord.BotToken)
            }
        });
    }

    /// <summary>
    /// Returns the current bot configuration (tokens masked).
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var telegram = telegramOptions.Value;
        var discord = discordOptions.Value;

        return Ok(new
        {
            telegram = new
            {
                enabled = telegram.Enabled,
                botToken = telegram.BotToken ?? ""
            },
            discord = new
            {
                enabled = discord.Enabled,
                botToken = discord.BotToken ?? ""
            }
        });
    }

    /// <summary>
    /// Updates bot configuration in the gateway <c>.env</c> file.
    /// The gateway must be restarted for changes to take full effect.
    /// </summary>
    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] BotConfigRequest request)
    {
        try
        {
            if (!System.IO.File.Exists(EnvFilePath))
                return StatusCode(500, new { error = "Gateway .env file not found." });

            var json = await System.IO.File.ReadAllTextAsync(EnvFilePath);

            // Strip JSON comments before parsing (// line comments)
            var cleaned = StripJsonComments(json);
            var root = JsonNode.Parse(cleaned);
            if (root is null)
                return StatusCode(500, new { error = "Failed to parse gateway .env." });

            // Ensure path exists: Gateway -> Bots -> Telegram / Discord
            var gateway = root["Gateway"]?.AsObject();
            if (gateway is null) { gateway = new JsonObject(); root["Gateway"] = gateway; }
            var bots = gateway["Bots"]?.AsObject();
            if (bots is null) { bots = new JsonObject(); gateway["Bots"] = bots; }

            if (request.Telegram is not null)
            {
                bots["Telegram"] = new JsonObject
                {
                    ["Enabled"] = request.Telegram.Enabled.ToString().ToLowerInvariant(),
                    ["BotToken"] = request.Telegram.BotToken ?? ""
                };
            }

            if (request.Discord is not null)
            {
                bots["Discord"] = new JsonObject
                {
                    ["Enabled"] = request.Discord.Enabled.ToString().ToLowerInvariant(),
                    ["BotToken"] = request.Discord.BotToken ?? ""
                };
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            await System.IO.File.WriteAllTextAsync(EnvFilePath, root.ToJsonString(options));

            return Ok(new { saved = true, restartRequired = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static string StripJsonComments(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                // Skip to end of line
                while (i < json.Length && json[i] != '\n')
                    i++;
                if (i < json.Length) sb.Append('\n');
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }
}

public sealed class BotConfigEntry
{
    public bool Enabled { get; set; }
    public string? BotToken { get; set; }
}

public sealed class BotConfigRequest
{
    public BotConfigEntry? Telegram { get; set; }
    public BotConfigEntry? Discord { get; set; }
}
