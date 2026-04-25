using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Microsoft.EntityFrameworkCore;

using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Modules.WebAccess.Enums;
using SharpClaw.Modules.WebAccess.Handlers;
using SharpClaw.Modules.WebAccess.Services;
using SharpClaw.Modules.WebAccess.Dtos;

namespace SharpClaw.Modules.WebAccess;

/// <summary>
/// Default module: localhost access (browser/CLI), external website access
/// with SSRF protection, and multi-provider search engine queries.
/// All platforms — uses standard .NET HTTP and System.Diagnostics.Process.
/// </summary>
public sealed class WebAccessModule : ISharpClawModule
{
    public string Id => "sharpclaw_web_access";
    public string DisplayName => "Web Access";
    public string ToolPrefix => "wa";

    // ═══════════════════════════════════════════════════════════════
    // DI Registration
    // ═══════════════════════════════════════════════════════════════

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped(sp => sp.GetRequiredService<IModuleDbContextFactory>()
            .CreateDbContext<WebAccessDbContext>());
        services.AddScoped<LocalhostAccessService>();
        services.AddScoped<WebsiteAccessService>();
        services.TryAddScoped<SearchEngineService>();
        services.TryAddScoped<WebsiteService>();
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
        new("WaWebsite", "WebsiteAccess", "AccessWebsiteAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<WebAccessDbContext>();
            return await db.Websites.Select(w => w.Id).ToListAsync(ct);
        },
        LoadLookupItems: static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<WebAccessDbContext>();
            return await db.Websites.Select(w => new ValueTuple<Guid, string>(w.Id, w.Name)).ToListAsync(ct);
        },
        DefaultResourceKey: "website"),
        new("WaSearch", "SearchEngineAccess", "QuerySearchEngineAsync", static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<WebAccessDbContext>();
            return await db.SearchEngines.Select(s => s.Id).ToListAsync(ct);
        },
        LoadLookupItems: static async (sp, ct) =>
        {
            var db = sp.GetRequiredService<WebAccessDbContext>();
            return await db.SearchEngines.Select(s => new ValueTuple<Guid, string>(s.Id, s.Name)).ToListAsync(ct);
        },
        DefaultResourceKey: "search"),
    ];

    // ═══════════════════════════════════════════════════════════════
    // Global Flag Descriptors
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleGlobalFlagDescriptor> GetGlobalFlagDescriptors() =>
    [
        new("CanAccessLocalhostInBrowser", "Access Localhost (Browser)", "Access localhost URLs through a headless browser.", "AccessLocalhostInBrowserAsync"),
        new("CanAccessLocalhostCli", "Access Localhost (CLI)", "Access localhost URLs via direct HTTP (no browser).", "AccessLocalhostCliAsync"),
    ];

    // ═══════════════════════════════════════════════════════════════
    // Tool Definitions
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions()
    {
        var globalLocalhostBrowser = new ModuleToolPermission(
            IsPerResource: false, Check: null,
            DelegateTo: "AccessLocalhostInBrowserAsync");

        var globalLocalhostCli = new ModuleToolPermission(
            IsPerResource: false, Check: null,
            DelegateTo: "AccessLocalhostCliAsync");

        var perResourceWebsite = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "AccessWebsiteAsync");

        var perResourceSearchEngine = new ModuleToolPermission(
            IsPerResource: true, Check: null,
            DelegateTo: "QuerySearchEngineAsync");

        return
        [
            new("access_localhost_browser",
                "Headless GET localhost. html=DOM (default), screenshot=PNG (vision). localhost/127.0.0.1 only.",
                BuildLocalhostBrowserSchema(), globalLocalhostBrowser,
                Aliases: ["access_localhost_in_browser"]),
            new("access_localhost_cli",
                "HTTP GET localhost; returns status+headers+body. localhost/127.0.0.1 only.",
                BuildLocalhostCliSchema(), globalLocalhostCli),
            new("access_website",
                "Fetch a registered external website. cli=HTTP GET (default), html=headless DOM, screenshot=PNG. " +
                "Optional path appends to the registered base URL. " +
                "Downloads are blocked; binary content types are rejected; redirects are pinned to the registered origin.",
                BuildAccessWebsiteSchema(), perResourceWebsite),
            new("query_search_engine",
                "Query a registered search engine. Parameters vary by engine type — " +
                "Google supports dateRestrict/siteRestrict/fileType/exactTerms/excludeTerms/searchType/sortBy; " +
                "Bing supports siteRestrict; SearXNG supports category; Tavily supports topic/searchType(basic|advanced); " +
                "all support query, count, offset, language, region, safeSearch.",
                BuildQuerySearchEngineSchema(), perResourceSearchEngine),
        ];
    }

    // ═══════════════════════════════════════════════════════════════
    // CLI Commands
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<ModuleCliCommand> GetCliCommands() =>
    [
        new(
            Name: "website",
            Aliases: ["site", "ws"],
            Scope: ModuleCliScope.ResourceType,
            Description: "Website resource management",
            UsageLines:
            [
                "resource website list                          List all websites",
                "resource website get <id>                      Show a website",
                "resource website add <name> <url> [desc]       Add a website",
                "resource website update <id> [fields...]       Update a website",
                "resource website delete <id>                   Delete a website",
            ],
            Handler: HandleResourceWebsiteCommandAsync),
        new(
            Name: "searchengine",
            Aliases: ["search", "se"],
            Scope: ModuleCliScope.ResourceType,
            Description: "Search engine resource management",
            UsageLines:
            [
                "resource searchengine list                              List all search engines",
                "resource searchengine get <id>                          Show a search engine",
                "resource searchengine add <name> <type> <endpoint>      Add a search engine",
                "resource searchengine update <id> [fields...]           Update a search engine",
                "resource searchengine delete <id>                       Delete a search engine",
            ],
            Handler: HandleResourceSearchEngineCommandAsync),
    ];

    // ── Website CLI handler ───────────────────────────────────────

    private static async Task HandleResourceWebsiteCommandAsync(
        string[] args, IServiceProvider sp, CancellationToken ct)
    {
        var ids = sp.GetRequiredService<ICliIdResolver>();
        var svc = sp.GetRequiredService<WebsiteService>();

        if (args.Length < 3)
        {
            PrintWebsiteUsage();
            return;
        }

        var sub = args[2].ToLowerInvariant();
        switch (sub)
        {
            case "add" when args.Length >= 5:
            {
                var result = await svc.CreateAsync(
                    args[3], args[4],
                    args.Length >= 6 ? string.Join(' ', args[5..]) : null,
                    ct);
                ids.PrintJson(result);
                break;
            }
            case "add":
                Console.Error.WriteLine("resource website add <name> <url> [description]");
                break;

            case "get" when args.Length >= 4:
            {
                var result = await svc.GetByIdAsync(ids.Resolve(args[3]), ct);
                if (result is not null)
                    ids.PrintJson(result);
                else
                    Console.Error.WriteLine("Not found.");
                break;
            }
            case "get":
                Console.Error.WriteLine("resource website get <id>");
                break;

            case "list":
            {
                var result = await svc.ListAsync(ct);
                ids.PrintJson(result);
                break;
            }

            case "update" when args.Length >= 5:
            {
                // resource website update <id> [name=X] [url=X] [desc=X]
                var id = ids.Resolve(args[3]);
                string? name = null, url = null, description = null;
                foreach (var arg in args[4..])
                {
                    if (arg.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        name = arg[5..];
                    else if (arg.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                        url = arg[4..];
                    else if (arg.StartsWith("desc=", StringComparison.OrdinalIgnoreCase))
                        description = arg[5..];
                    else
                        name ??= arg; // first bare arg = name
                }
                var result = await svc.UpdateAsync(id, name, url, description, ct);
                if (result is not null)
                    ids.PrintJson(result);
                else
                    Console.Error.WriteLine("Not found.");
                break;
            }
            case "update":
                Console.Error.WriteLine("resource website update <id> [name=X] [url=X] [desc=X]");
                break;

            case "delete" when args.Length >= 4:
            {
                var deleted = await svc.DeleteAsync(ids.Resolve(args[3]), ct);
                Console.WriteLine(deleted ? "Done." : "Not found.");
                break;
            }
            case "delete":
                Console.Error.WriteLine("resource website delete <id>");
                break;

            default:
                Console.Error.WriteLine($"Unknown command: resource website {sub}");
                PrintWebsiteUsage();
                break;
        }
    }

    private static void PrintWebsiteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  resource website list                          List all websites");
        Console.Error.WriteLine("  resource website get <id>                      Show a website");
        Console.Error.WriteLine("  resource website add <name> <url> [desc]       Add a website");
        Console.Error.WriteLine("  resource website update <id> [name=X] [url=X]  Update a website");
        Console.Error.WriteLine("  resource website delete <id>                   Delete a website");
    }

    // ── Search engine CLI handler ─────────────────────────────────

    private static async Task HandleResourceSearchEngineCommandAsync(
        string[] args, IServiceProvider sp, CancellationToken ct)
    {
        var ids = sp.GetRequiredService<ICliIdResolver>();
        var svc = sp.GetRequiredService<SearchEngineService>();

        if (args.Length < 3)
        {
            PrintSearchEngineUsage();
            return;
        }

        var sub = args[2].ToLowerInvariant();
        switch (sub)
        {
            case "add" when args.Length >= 6:
            {
                if (!Enum.TryParse<SearchEngineType>(args[4], true, out var engineType))
                {
                    Console.Error.WriteLine($"Unknown search engine type: '{args[4]}'.");
                    Console.Error.WriteLine($"Valid types: {string.Join(", ", Enum.GetNames<SearchEngineType>())}");
                    return;
                }
                var result = await svc.CreateAsync(
                    new CreateSearchEngineRequest(
                        args[3], engineType, args[5]), ct);
                ids.PrintJson(result);
                break;
            }
            case "add":
                Console.Error.WriteLine("resource searchengine add <name> <type> <endpoint>");
                break;

            case "get" when args.Length >= 4:
            {
                var result = await svc.GetByIdAsync(ids.Resolve(args[3]), ct);
                if (result is not null)
                    ids.PrintJson(result);
                else
                    Console.Error.WriteLine("Not found.");
                break;
            }
            case "get":
                Console.Error.WriteLine("resource searchengine get <id>");
                break;

            case "list":
            {
                var result = await svc.ListAsync(ct);
                ids.PrintJson(result);
                break;
            }

            case "update" when args.Length >= 5:
            {
                var id = ids.Resolve(args[3]);
                string? name = null, endpoint = null, description = null;
                SearchEngineType? type = null;
                foreach (var arg in args[4..])
                {
                    if (arg.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        name = arg[5..];
                    else if (arg.StartsWith("endpoint=", StringComparison.OrdinalIgnoreCase))
                        endpoint = arg[9..];
                    else if (arg.StartsWith("type=", StringComparison.OrdinalIgnoreCase)
                             && Enum.TryParse<SearchEngineType>(arg[5..], true, out var t))
                        type = t;
                    else if (arg.StartsWith("desc=", StringComparison.OrdinalIgnoreCase))
                        description = arg[5..];
                    else
                        name ??= arg;
                }
                var result = await svc.UpdateAsync(id,
                    new UpdateSearchEngineRequest(
                        name, type, endpoint, description), ct);
                if (result is not null)
                    ids.PrintJson(result);
                else
                    Console.Error.WriteLine("Not found.");
                break;
            }
            case "update":
                Console.Error.WriteLine("resource searchengine update <id> [name=X] [type=X] [endpoint=X]");
                break;

            case "delete" when args.Length >= 4:
            {
                var deleted = await svc.DeleteAsync(ids.Resolve(args[3]), ct);
                Console.WriteLine(deleted ? "Done." : "Not found.");
                break;
            }
            case "delete":
                Console.Error.WriteLine("resource searchengine delete <id>");
                break;

            default:
                Console.Error.WriteLine($"Unknown command: resource searchengine {sub}");
                PrintSearchEngineUsage();
                break;
        }
    }

    private static void PrintSearchEngineUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  resource searchengine list                              List all search engines");
        Console.Error.WriteLine("  resource searchengine get <id>                          Show a search engine");
        Console.Error.WriteLine("  resource searchengine add <name> <type> <endpoint>      Add a search engine");
        Console.Error.WriteLine("  resource searchengine update <id> [name=X] [type=X]     Update a search engine");
        Console.Error.WriteLine("  resource searchengine delete <id>                       Delete a search engine");
    }

    // ═══════════════════════════════════════════════════════════════
    // Endpoint Mapping
    // ═══════════════════════════════════════════════════════════════

    public void MapEndpoints(object app)
    {
        var endpoints = (Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app;
        endpoints.MapWebsiteEndpoints();
        endpoints.MapSearchEngineEndpoints();
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
            "access_localhost_browser" or "access_localhost_in_browser"
                => await sp.GetRequiredService<LocalhostAccessService>()
                    .AccessBrowserAsync(parameters, job, ct),

            "access_localhost_cli"
                => await sp.GetRequiredService<LocalhostAccessService>()
                    .AccessCliAsync(parameters, job, ct),

            "access_website"
                => await sp.GetRequiredService<WebsiteAccessService>()
                    .AccessAsync(parameters, job, ct),

            "query_search_engine"
                => await sp.GetRequiredService<SearchEngineService>()
                    .ExecuteQueryAsync(parameters, job, ct),

            _ => throw new InvalidOperationException(
                $"Unknown Web Access tool: '{toolName}'."),
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Lifecycle
    // ═══════════════════════════════════════════════════════════════

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;

    public async Task SeedDataAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<WebAccessDbContext>();

        var existing = await db.SearchEngines
            .Select(e => e.Type)
            .ToHashSetAsync(ct);

        var defaultEndpoints = new Dictionary<SearchEngineType, string>
        {
            [SearchEngineType.Google]    = "https://www.googleapis.com/customsearch/v1",
            [SearchEngineType.Bing]      = "https://api.bing.microsoft.com/v7.0/search",
            [SearchEngineType.DuckDuckGo] = "https://api.duckduckgo.com/",
            [SearchEngineType.Brave]     = "https://api.search.brave.com/res/v1/web/search",
            [SearchEngineType.SearXNG]   = "https://searx.be/search",
            [SearchEngineType.Tavily]    = "https://api.tavily.com/search",
            [SearchEngineType.Serper]    = "https://google.serper.dev/search",
            [SearchEngineType.Kagi]      = "https://kagi.com/api/v0/search",
            [SearchEngineType.YouDotCom] = "https://api.ydc-index.io/search",
            [SearchEngineType.Mojeek]    = "https://www.mojeek.com/search",
            [SearchEngineType.Yandex]    = "https://yandex.com/search/xml",
            [SearchEngineType.Baidu]     = "https://api.baidu.com/search",
        };

        var toSeed = Enum.GetValues<SearchEngineType>()
            .Where(t => t != SearchEngineType.Custom && !existing.Contains(t))
            .ToList();

        if (toSeed.Count == 0)
            return;

        foreach (var type in toSeed)
        {
            db.SearchEngines.Add(new Models.SearchEngineDB
            {
                Name = type.ToString(),
                Type = type,
                Endpoint = defaultEndpoints.GetValueOrDefault(type, string.Empty),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public Task ShutdownAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════════
    // Schema builders
    // ═══════════════════════════════════════════════════════════════

    private static JsonElement BuildLocalhostBrowserSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": {
                        "type": "string",
                        "description": "Localhost URL."
                    },
                    "mode": {
                        "type": "string",
                        "enum": ["html", "screenshot"],
                        "description": "'html' (default)=DOM, 'screenshot'=PNG."
                    }
                },
                "required": ["url"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildLocalhostCliSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": {
                        "type": "string",
                        "description": "Localhost URL."
                    }
                },
                "required": ["url"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildAccessWebsiteSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "targetId": {
                        "type": "string",
                        "description": "Website resource GUID."
                    },
                    "mode": {
                        "type": "string",
                        "enum": ["cli", "html", "screenshot"],
                        "description": "'cli' (default)=HTTP GET with headers+body, 'html'=headless browser DOM, 'screenshot'=headless browser PNG."
                    },
                    "path": {
                        "type": "string",
                        "description": "Optional path appended to the registered base URL (e.g. '/api/v1/status')."
                    }
                },
                "required": ["targetId"]
            }
            """);
        return doc.RootElement.Clone();
    }

    private static JsonElement BuildQuerySearchEngineSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "targetId": {
                        "type": "string",
                        "description": "Search engine resource GUID."
                    },
                    "query": {
                        "type": "string",
                        "description": "Search query text."
                    },
                    "count": {
                        "type": "integer",
                        "description": "Max results to return (default 10)."
                    },
                    "offset": {
                        "type": "integer",
                        "description": "Result offset for pagination (default 0)."
                    },
                    "language": {
                        "type": "string",
                        "description": "Language code (e.g. 'en', 'lang_en' for Google, BCP-47 for others)."
                    },
                    "region": {
                        "type": "string",
                        "description": "Region/market code (e.g. 'us', 'en-US' for Bing)."
                    },
                    "safeSearch": {
                        "type": "string",
                        "description": "Safe search level. Google: off/medium/high. Bing: Off/Moderate/Strict. Brave: off/moderate/strict. SearXNG: 0/1/2."
                    },
                    "dateRestrict": {
                        "type": "string",
                        "description": "Google only. Restrict by date: d[N], w[N], m[N], y[N]."
                    },
                    "siteRestrict": {
                        "type": "string",
                        "description": "Google/Bing: restrict to a specific site domain."
                    },
                    "fileType": {
                        "type": "string",
                        "description": "Google only. Filter by file type (e.g. 'pdf', 'doc')."
                    },
                    "exactTerms": {
                        "type": "string",
                        "description": "Google only. Phrase that must appear in results."
                    },
                    "excludeTerms": {
                        "type": "string",
                        "description": "Google only. Terms to exclude from results."
                    },
                    "searchType": {
                        "type": "string",
                        "description": "Google: 'image' for image search. Tavily: 'basic' or 'advanced'."
                    },
                    "sortBy": {
                        "type": "string",
                        "description": "Google only. Sort order (e.g. 'date')."
                    },
                    "topic": {
                        "type": "string",
                        "description": "Tavily only. Topic filter: 'general' or 'news'."
                    },
                    "category": {
                        "type": "string",
                        "description": "SearXNG only. Category: general, images, news, etc."
                    }
                },
                "required": ["targetId", "query"]
            }
            """);
        return doc.RootElement.Clone();
    }
}
