using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Entities.Core.Access;
using SharpClaw.Contracts.Entities.Core.Clearance;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.Entities.Core.Messages;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Modules.AgentOrchestration;

namespace SharpClaw.Tests.Services;

public sealed class CrossThreadHistoryTests
{
    [Test]
    public async Task AdminRoleAgentCanListAndReadAnotherDirectChannelThread()
    {
        await using var provider = CreateContextToolProvider(out var module);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();

        var agent = SeedAgentWithCrossThreadRole(db, PermissionClearance.Independent);
        var currentChannel = MakeChannel("Current Channel", agent.Id);
        var sourceChannel = MakeChannel("Source Channel", agent.Id);
        var sourceThread = MakeThread("Source Thread", sourceChannel.Id);
        var sourceMessage = MakeMessage(sourceChannel.Id, sourceThread.Id, "hello from source channel");

        db.Channels.AddRange(currentChannel, sourceChannel);
        db.ChatThreads.Add(sourceThread);
        db.ChatMessages.Add(sourceMessage);
        await db.SaveChangesAsync();

        using var empty = JsonDocument.Parse("{}");
        var inlineContext = new InlineToolContext(agent.Id, currentChannel.Id, null, "call-list");

        var listResult = await module.ExecuteInlineToolAsync(
            "list_accessible_threads", empty.RootElement, inlineContext, scope.ServiceProvider, default);

        listResult.Should().Contain(sourceThread.Id.ToString("D"));
        listResult.Should().Contain("Source Channel");

        using var readArgs = JsonDocument.Parse(
            $$"""{"threadId":"{{sourceThread.Id:D}}","maxMessages":10}""");

        var readResult = await module.ExecuteInlineToolAsync(
            "read_thread_history", readArgs.RootElement, inlineContext, scope.ServiceProvider, default);

        readResult.Should().Contain("hello from source channel");
    }

    [Test]
    public async Task ContextDefaultAgentCountsAsChannelAssignmentForCrossThreadDiscovery()
    {
        await using var provider = CreateContextToolProvider(out var module);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();

        var agent = SeedAgentWithCrossThreadRole(db, PermissionClearance.Independent);
        var context = new ChannelContextDB
        {
            Id = Guid.NewGuid(),
            Name = "Shared Context",
            AgentId = agent.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var currentChannel = MakeChannel("Current Channel", agent.Id);
        var sourceChannel = MakeChannel("Context Source Channel", agentId: null);
        sourceChannel.AgentContextId = context.Id;
        var sourceThread = MakeThread("Context Source Thread", sourceChannel.Id);

        db.AgentContexts.Add(context);
        db.Channels.AddRange(currentChannel, sourceChannel);
        db.ChatThreads.Add(sourceThread);
        await db.SaveChangesAsync();

        using var empty = JsonDocument.Parse("{}");
        var result = await module.ExecuteInlineToolAsync(
            "list_accessible_threads",
            empty.RootElement,
            new InlineToolContext(agent.Id, currentChannel.Id, null, "call-list"),
            scope.ServiceProvider,
            default);

        result.Should().Contain(sourceThread.Id.ToString("D"));
        result.Should().Contain("Context Source Channel");
    }

    private static ServiceProvider CreateContextToolProvider(out AgentOrchestrationModule module)
    {
        module = new AgentOrchestrationModule();
        var services = new ServiceCollection();
        services.AddDbContext<SharpClawDbContext>(
            options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<ISharpClawDataContext>(
            sp => sp.GetRequiredService<SharpClawDbContext>());
        module.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static AgentDB SeedAgentWithCrossThreadRole(
        SharpClawDbContext db, PermissionClearance clearance)
    {
        var permissionSet = new PermissionSetDB
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            GlobalFlags =
            [
                new GlobalFlagDB
                {
                    Id = Guid.NewGuid(),
                    FlagKey = ContextToolsPermissionKeys.CanReadCrossThreadHistory,
                    Clearance = clearance,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        var role = new RoleDB
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            PermissionSetId = permissionSet.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var agent = new AgentDB
        {
            Id = Guid.NewGuid(),
            Name = $"CrossThreadAgent-{Guid.NewGuid():N}",
            ModelId = Guid.NewGuid(),
            RoleId = role.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.PermissionSets.Add(permissionSet);
        db.Roles.Add(role);
        db.Agents.Add(agent);
        return agent;
    }

    private static ChannelDB MakeChannel(string title, Guid? agentId) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        AgentId = agentId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ChatThreadDB MakeThread(string name, Guid channelId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ChannelId = channelId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ChatMessageDB MakeMessage(Guid channelId, Guid threadId, string content) => new()
    {
        Id = Guid.NewGuid(),
        Role = "user",
        Content = content,
        ChannelId = channelId,
        ThreadId = threadId,
        SenderUsername = "tester",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
