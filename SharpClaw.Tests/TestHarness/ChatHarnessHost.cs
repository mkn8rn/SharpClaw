using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Application.Core.Clients;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Entities.Core.Access;
using SharpClaw.Contracts.Entities.Core.Clearance;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Modules.TestHarness;

namespace SharpClaw.Tests.TestHarness;

internal sealed class ChatHarnessHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    private ChatHarnessHost(ServiceProvider root, AsyncServiceScope scope)
    {
        _root = root;
        _scope = scope;
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public SharpClawDbContext Db => Services.GetRequiredService<SharpClawDbContext>();
    public ChatService Chat => Services.GetRequiredService<ChatService>();
    public TestHarnessState Harness => _root.GetRequiredService<TestHarnessState>();
    public CountingPersistenceEntityResolver PersistenceCounter =>
        _root.GetRequiredService<CountingPersistenceEntityResolver>();

    public static ChatHarnessHost Create(
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var module = new TestHarnessModule();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton(new EncryptionOptions
        {
            Key = new byte[32],
            EncryptProviderKeys = false
        });
        services.AddDbContext<SharpClawDbContext>(
            options => options.UseInMemoryDatabase(
                "SharpClawHarness_" + Guid.NewGuid().ToString("N"),
                new InMemoryDatabaseRoot()));
        services.AddSingleton<ModuleRegistry>();
        services.AddSingleton<ModuleMetricsCollector>();
        services.AddSingleton<ThreadActivitySignal>();
        services.AddSingleton<ChatCache>();
        services.AddSingleton<ProviderApiClientFactory>();
        services.AddSingleton<CountingPersistenceEntityResolver>();
        services.AddScoped<IPersistenceEntityResolver>(
            sp => sp.GetRequiredService<CountingPersistenceEntityResolver>());
        services.AddScoped<SessionService>();
        services.AddScoped<AgentActionService>();
        services.AddScoped<AgentJobService>();
        services.AddScoped<HeaderTagProcessor>();
        services.AddScoped<ChatService>();
        services.AddScoped<ModuleExecutionContext>();
        services.AddSingleton<ModuleEventDispatcher>(sp => new ModuleEventDispatcher(
            sp,
            sp.GetRequiredService<IConfiguration>(),
            NullLogger<ModuleEventDispatcher>.Instance));

        module.ConfigureServices(services);

        var root = services.BuildServiceProvider();
        var registry = root.GetRequiredService<ModuleRegistry>();
        registry.Register(module);

        return new ChatHarnessHost(root, root.CreateAsyncScope());
    }

    public async Task<SeededChat> SeedChatAsync(
        string providerKey,
        bool grantHarnessPermission = false,
        PermissionClearance clearance = PermissionClearance.Independent,
        string? agentSystemPrompt = "agent system",
        string? customHeader = null,
        bool disableToolSchemas = false,
        bool includeUser = true,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new ProviderDB
        {
            Id = Guid.NewGuid(),
            Name = "Harness Provider",
            ProviderKey = providerKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        var model = new ModelDB
        {
            Id = Guid.NewGuid(),
            Name = TestHarnessConstants.ModelId,
            ProviderId = provider.Id,
            Provider = provider,
            CapabilityTagsRaw = "chat",
            CreatedAt = now,
            UpdatedAt = now
        };

        var permissionSet = new PermissionSetDB
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        if (grantHarnessPermission)
        {
            permissionSet.GlobalFlags.Add(new GlobalFlagDB
            {
                Id = Guid.NewGuid(),
                FlagKey = TestHarnessConstants.GlobalFlagKey,
                Clearance = clearance,
                PermissionSetId = permissionSet.Id,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var role = new RoleDB
        {
            Id = Guid.NewGuid(),
            Name = "Harness Role",
            PermissionSetId = permissionSet.Id,
            PermissionSet = permissionSet,
            CreatedAt = now,
            UpdatedAt = now
        };
        var agent = new AgentDB
        {
            Id = Guid.NewGuid(),
            Name = "Harness Agent",
            ModelId = model.Id,
            Model = model,
            RoleId = role.Id,
            Role = role,
            SystemPrompt = agentSystemPrompt,
            CustomChatHeader = customHeader,
            DisableToolSchemas = disableToolSchemas,
            CreatedAt = now,
            UpdatedAt = now
        };
        var channel = new ChannelDB
        {
            Id = Guid.NewGuid(),
            Title = "Harness Channel",
            AgentId = agent.Id,
            Agent = agent,
            CreatedAt = now,
            UpdatedAt = now
        };

        UserDB? user = null;
        if (includeUser)
        {
            user = new UserDB
            {
                Id = Guid.NewGuid(),
                Username = "harness-user",
                PasswordHash = [1],
                PasswordSalt = [2],
                RoleId = role.Id,
                Role = role,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.Users.Add(user);
            Services.GetRequiredService<SessionService>().UserId = user.Id;
        }

        Db.Providers.Add(provider);
        Db.Models.Add(model);
        Db.PermissionSets.Add(permissionSet);
        Db.Roles.Add(role);
        Db.Agents.Add(agent);
        Db.Channels.Add(channel);
        await Db.SaveChangesAsync(ct);

        return new SeededChat(provider, model, agent, channel, role, permissionSet, user);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }
}

internal sealed record SeededChat(
    ProviderDB Provider,
    ModelDB Model,
    AgentDB Agent,
    ChannelDB Channel,
    RoleDB Role,
    PermissionSetDB PermissionSet,
    UserDB? User);

internal sealed class CountingPersistenceEntityResolver : IPersistenceEntityResolver
{
    private readonly EfPersistenceEntityResolver _inner = new();

    public int FindCalls { get; private set; }
    public int QueryCalls { get; private set; }

    public void Reset()
    {
        FindCalls = 0;
        QueryCalls = 0;
    }

    public async Task<T?> FindAsync<T>(
        SharpClawDbContext db,
        Guid id,
        CancellationToken ct = default)
        where T : BaseEntity
    {
        FindCalls++;
        return await _inner.FindAsync<T>(db, id, ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        SharpClawDbContext db,
        Expression<Func<T, bool>> predicate,
        PersistenceQueryHint? hint = null,
        CancellationToken ct = default)
        where T : BaseEntity
    {
        QueryCalls++;
        return await _inner.QueryAsync(db, predicate, hint, ct);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        SharpClawDbContext db,
        Expression<Func<T, bool>> predicate,
        int limit,
        PersistenceQueryHint? hint = null,
        CancellationToken ct = default)
        where T : BaseEntity
    {
        QueryCalls++;
        return await _inner.QueryAsync(db, predicate, limit, hint, ct);
    }
}

internal static class HarnessBudget
{
    public static void AssertWithin(
        long measuredMs,
        int configuredProviderMs,
        int allowedOverheadMs,
        string surface)
    {
        var budget = configuredProviderMs + allowedOverheadMs;
        measuredMs.Should().BeLessThanOrEqualTo(
            budget,
            $"{surface} has a hard budget of provider time ({configuredProviderMs}ms) plus overhead ({allowedOverheadMs}ms)");
    }
}
