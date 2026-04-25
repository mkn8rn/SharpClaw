using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpClaw.Modules.BotIntegration.Contracts;
using SharpClaw.Modules.BotIntegration.Dtos;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Modules.BotIntegration.Handlers;
using SharpClaw.Modules.BotIntegration.Services;

namespace SharpClaw.Modules.BotIntegration;

/// <summary>
/// Default module: outbound bot messaging to Telegram, Discord, WhatsApp,
/// Slack, Matrix, Signal, Email, and Teams. Includes bot integration CRUD.
/// All platforms — all senders use standard HTTP or SMTP.
/// </summary>
public sealed class BotIntegrationModule : ISharpClawModule
{
    public string Id => "sharpclaw_bot_integration";
    public string DisplayName => "Bot Integration";
    public string ToolPrefix => "bi";

    // ═══════════════════════════════════════════════════════════════
    // DI Registration
    // ═══════════════════════════════════════════════════════════════

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped(sp => sp.GetRequiredService<IModuleDbContextFactory>()
            .CreateDbContext<BotIntegrationDbContext>());
        services.TryAddScoped<BotIntegrationService>();
        services.TryAddScoped<BotMessageSenderService>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Contracts
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleContractExport> ExportedContracts => [];

    // ═══════════════════════════════════════════════════════════════
    // Resource Type Descriptors
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleResourceTypeDescriptor> GetResourceTypeDescriptors() =>
    [
        new("BiChannel", "BotIntegration", "AccessBotIntegrationAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<BotIntegrationDbContext>();
            return await db.BotIntegrations.Select(b => b.Id).ToListAsync(ct);
        },
        LoadLookupItems: static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<BotIntegrationDbContext>();
            return await db.BotIntegrations.Select(b => new ValueTuple<Guid, string>(b.Id, b.Name)).ToListAsync(ct);
        }, DefaultResourceKey: "botintegration"),
    ];

    // ═══════════════════════════════════════════════════════════════
    // Tool Definitions
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions()
    {
        var perResourceBotIntegration = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "AccessBotIntegrationAsync");

        return
        [
            new("send_bot_message",
                "Send DM via bot (Telegram/Discord/WhatsApp/Slack/Matrix/Signal/Email/Teams). " +
                "recipientId is platform-specific; subject for email only.",
                BuildSendBotMessageSchema(), perResourceBotIntegration),
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    // CLI Commands
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleCliCommand> GetCliCommands() =>
    [
        new(
            Name: "bot",
            Aliases: [],
            Scope: ModuleCliScope.TopLevel,
            Description: "Bot integration management",
            UsageLines:
            [
                "bot list                                  List all bot integrations",
                "bot get <id>                              Show a bot integration",
                "bot update <id> [--name <n>] [--enabled true|false] [--token <tok>] [--channel <channelId>]",
                "                                          Update a bot integration",
                "bot config <type>                         Show decrypted config (telegram|discord|whatsapp|...)",
            ],
            Handler: HandleBotCommandAsync),
    ];

    // ── Bot CLI handler ───────────────────────────────────────────

    private static async Task HandleBotCommandAsync(
        string[] args, IServiceProvider sp, CancellationToken ct)
    {
        var ids = sp.GetRequiredService<ICliIdResolver>();
        var svc = sp.GetRequiredService<BotIntegrationService>();

        if (args.Length < 2)
        {
            PrintBotUsage();
            return;
        }

        var sub = args[1].ToLowerInvariant();
        switch (sub)
        {
            case "list":
            {
                var result = await svc.ListAsync(ct);
                ids.PrintJson(result);
                break;
            }

            case "get" when args.Length >= 3:
            {
                var result = await svc.GetByIdAsync(ids.Resolve(args[2]), ct);
                if (result is not null)
                    ids.PrintJson(result);
                else
                    Console.Error.WriteLine("Not found.");
                break;
            }
            case "get":
                Console.Error.WriteLine("bot get <id>");
                break;

            case "update" when args.Length >= 3:
            {
                var id = ids.Resolve(args[2]);
                string? name = null;
                bool? enabled = null;
                string? token = null;
                Guid? channelId = null;

                for (var i = 3; i < args.Length - 1; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--name":
                            name = args[++i]; break;
                        case "--enabled" when bool.TryParse(args[i + 1], out var e):
                            enabled = e; i++; break;
                        case "--token":
                            token = args[++i]; break;
                        case "--channel" when Guid.TryParse(args[i + 1], out var ch):
                            channelId = ch; i++; break;
                        case "--channel" when args[i + 1].ToLowerInvariant() is "none" or "clear":
                            channelId = Guid.Empty; i++; break;
                    }
                }

                var request = new UpdateBotIntegrationRequest(name, enabled, token, channelId);
                try
                {
                    var result = await svc.UpdateAsync(id, request, ct);
                    ids.PrintJson(result);
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                }
                break;
            }
            case "update":
                Console.Error.WriteLine("bot update <id> [--name <n>] [--enabled true|false] [--token <tok>] [--channel <channelId>]");
                break;

            case "config" when args.Length >= 3:
            {
                if (!Enum.TryParse<BotType>(args[2], ignoreCase: true, out var botType))
                {
                    Console.Error.WriteLine($"Unknown bot type: {args[2]}");
                    Console.Error.WriteLine($"Valid types: {string.Join(", ", Enum.GetNames<BotType>())}");
                    return;
                }
                var (botEnabled, botToken, defaultChannelId, defaultThreadId, platformConfig) =
                    await svc.GetBotConfigAsync(botType, ct);
                ids.PrintJson(new { enabled = botEnabled, botToken = botToken ?? "", defaultChannelId, defaultThreadId, platformConfig });
                break;
            }
            case "config":
                Console.Error.WriteLine("bot config <type>  (telegram|discord|whatsapp|...)");
                break;

            default:
                Console.Error.WriteLine($"Unknown command: bot {sub}");
                PrintBotUsage();
                break;
        }
    }

    private static void PrintBotUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  bot list                                  List all bot integrations");
        Console.Error.WriteLine("  bot get <id>                              Show a bot integration");
        Console.Error.WriteLine("  bot update <id> [--name] [--enabled] [--token] [--channel]");
        Console.Error.WriteLine("                                            Update a bot integration");
        Console.Error.WriteLine("  bot config <type>                         Show decrypted config");
    }

    // ═══════════════════════════════════════════════════════════════
    // Endpoint Mapping
    // ═══════════════════════════════════════════════════════════════

    public void MapEndpoints(object app)
    {
        var endpoints = (Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app;
        endpoints.MapBotEndpoints();
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool Execution
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ExecuteToolAsync(
        string toolName, JsonElement parameters, AgentJobContext job,
        IServiceProvider sp, CancellationToken ct)
    {
        return toolName switch
        {
            "send_bot_message"
                => await ExecuteSendBotMessageAsync(parameters, job, sp, ct),

            _ => throw new InvalidOperationException(
                $"Unknown Bot Integration tool: '{toolName}'."),
        };
    }

    private static async Task<string> ExecuteSendBotMessageAsync(
        JsonElement parameters, AgentJobContext job,
        IServiceProvider sp, CancellationToken ct)
    {
        var recipientId = parameters.TryGetProperty("recipientId", out var rProp)
            ? rProp.GetString() : null;
        var message = parameters.TryGetProperty("message", out var mProp)
            ? mProp.GetString() : null;
        var subject = parameters.TryGetProperty("subject", out var sProp)
            ? sProp.GetString() : null;

        if (string.IsNullOrWhiteSpace(recipientId))
            throw new InvalidOperationException("send_bot_message requires a 'recipientId' parameter.");
        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("send_bot_message requires a 'message' parameter.");
        if (!job.ResourceId.HasValue)
            throw new InvalidOperationException("send_bot_message requires a ResourceId (bot integration ID).");

        var sender = sp.GetRequiredService<BotMessageSenderService>();
        await sender.SendMessageAsync(
            job.ResourceId.Value, recipientId, message, subject, ct);

        return $"Message sent successfully via bot integration {job.ResourceId} to recipient '{recipientId}'.";
    }

    // ═══════════════════════════════════════════════════════════════
    // Lifecycle
    // ═══════════════════════════════════════════════════════════════

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;

    public async Task SeedDataAsync(IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BotIntegrationService>();
        await svc.EnsureAllTypesExistAsync(ct);
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════════
    // Schema builders
    // ═══════════════════════════════════════════════════════════════

    private static JsonElement BuildSendBotMessageSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resourceId": {
                        "type": "string",
                        "description": "Bot integration GUID."
                    },
                    "recipientId": {
                        "type": "string",
                        "description": "Platform-specific recipient: Telegram chat ID, Discord user ID, WhatsApp phone (E.164), Slack user ID, Matrix user ID (@user:server), Signal phone (E.164), email address, or Teams user ID."
                    },
                    "message": {
                        "type": "string",
                        "description": "Message text to send."
                    },
                    "subject": {
                        "type": "string",
                        "description": "Email subject line (email only, optional)."
                    }
                },
                "required": ["resourceId", "recipientId", "message"]
            }
            """);
        return doc.RootElement.Clone();
    }
}
