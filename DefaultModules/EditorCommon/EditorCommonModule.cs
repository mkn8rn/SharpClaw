using System.Text.Json;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Modules.EditorCommon.Handlers;
using SharpClaw.Modules.EditorCommon.Services;

namespace SharpClaw.Modules.EditorCommon;

/// <summary>
/// Infrastructure module: shared editor bridge and session services
/// consumed by the VS 2026 and VS Code editor modules.
/// No LLM-callable tools — this module provides only DI services,
/// contract exports, and REST/WebSocket endpoints.
/// </summary>
public sealed class EditorCommonModule : ISharpClawModule
{
    public string Id => "sharpclaw_editor_common";
    public string DisplayName => "Editor Common";
    public string ToolPrefix => "edc";

    // ═══════════════════════════════════════════════════════════════
    // Contract Exports
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleContractExport> ExportedContracts =>
    [
        new("editor_bridge", typeof(EditorBridgeService),
            "WebSocket-based IDE bridge for editor extensions"),
        new("editor_session", typeof(EditorSessionService),
            "Editor session CRUD management"),
    ];

    // ═══════════════════════════════════════════════════════════════
    // DI Registration
    // ═══════════════════════════════════════════════════════════════

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped(sp => sp.GetRequiredService<IModuleDbContextFactory>()
            .CreateDbContext<EditorCommonDbContext>());
        services.AddSingleton<EditorBridgeService>();
        services.AddScoped<EditorSessionService>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Endpoint Registration
    // ═══════════════════════════════════════════════════════════════

    public void MapEndpoints(object app)
    {
        var endpoints = (IEndpointRouteBuilder)app;
        endpoints.MapEditorEndpoints();
        endpoints.MapEditorSessionResourceEndpoints();
    }

    // ═══════════════════════════════════════════════════════════════
    // CLI Commands
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleCliCommand> GetCliCommands() =>
    [
        new(
            Name: "editorsession",
            Aliases: ["editor", "es"],
            Scope: ModuleCliScope.ResourceType,
            Description: "Editor session CRUD",
            UsageLines:
            [
                "resource editorsession list                      List all editor sessions",
                "resource editorsession get <id>                  Show an editor session",
                "resource editorsession delete <id>               Delete an editor session",
            ],
            Handler: HandleEditorSessionResourceCliAsync),
    ];

    private static async Task HandleEditorSessionResourceCliAsync(
        string[] args, IServiceProvider sp, CancellationToken ct)
    {
        var ids = sp.GetRequiredService<ICliIdResolver>();

        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  resource editorsession list                      List all editor sessions");
            Console.Error.WriteLine("  resource editorsession get <id>                  Show an editor session");
            Console.Error.WriteLine("  resource editorsession delete <id>               Delete an editor session");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Editor sessions are auto-created when an IDE extension connects.");
            Console.Error.WriteLine("Use 'channel defaults <id> set editor <sessionId>' to assign one.");
            return;
        }

        var sub = args[2].ToLowerInvariant();
        var svc = sp.GetRequiredService<EditorSessionService>();

        switch (sub)
        {
            case "get" when args.Length >= 4:
                var session = await svc.GetByIdAsync(ids.Resolve(args[3]), ct);
                if (session is null) { Console.Error.WriteLine("Not found."); return; }
                ids.PrintJson(session);
                break;
            case "get":
                Console.Error.WriteLine("resource editorsession get <id>");
                break;

            case "list":
                ids.PrintJson(await svc.ListAsync(ct));
                break;

            case "delete" when args.Length >= 4:
                Console.WriteLine(
                    await svc.DeleteAsync(ids.Resolve(args[3]))
                        ? "Done." : "Not found.");
                break;
            case "delete":
                Console.Error.WriteLine("resource editorsession delete <id>");
                break;

            default:
                Console.Error.WriteLine($"Unknown command: resource editorsession {sub}");
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // No LLM-callable tools
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

    public Task<string> ExecuteToolAsync(
        string toolName, JsonElement parameters,
        AgentJobContext job, IServiceProvider scopedServices,
        CancellationToken ct) =>
        throw new NotSupportedException(
            $"EditorCommon does not expose LLM-callable tools (received '{toolName}').");
}
