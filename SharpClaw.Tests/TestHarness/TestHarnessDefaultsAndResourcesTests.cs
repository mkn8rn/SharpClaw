using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.DefaultResources;
using SharpClaw.Contracts.Modules;
using SharpClaw.Modules.TestHarness;

namespace SharpClaw.Tests.TestHarness;

[TestFixture]
public sealed class TestHarnessDefaultsAndResourcesTests
{
    [Test]
    public async Task ChannelDefaultsClearRemovesKeyPermanently()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var service = host.Services.GetRequiredService<DefaultResourceSetService>();
        var resourceId = Guid.NewGuid();

        await service.SetKeyForChannelAsync(
            seeded.Channel.Id,
            TestHarnessConstants.DefaultResourceKey,
            resourceId);
        await service.ClearKeyForChannelAsync(
            seeded.Channel.Id,
            TestHarnessConstants.DefaultResourceKey);

        var result = await service.GetForChannelAsync(seeded.Channel.Id);
        result!.Entries.Should().NotContainKey(TestHarnessConstants.DefaultResourceKey);
    }

    [Test]
    public async Task ContextDefaultsClearRemovesKeyPermanently()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var context = new SharpClaw.Contracts.Entities.Core.Context.ChannelContextDB
        {
            Id = Guid.NewGuid(),
            Name = "Defaults Context",
            AgentId = seeded.Agent.Id,
            Agent = seeded.Agent,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        host.Db.AgentContexts.Add(context);
        await host.Db.SaveChangesAsync();
        var service = host.Services.GetRequiredService<DefaultResourceSetService>();

        await service.SetKeyForContextAsync(
            context.Id,
            TestHarnessConstants.DefaultResourceKey,
            Guid.NewGuid());
        await service.ClearKeyForContextAsync(
            context.Id,
            TestHarnessConstants.DefaultResourceKey);

        var result = await service.GetForContextAsync(context.Id);
        result!.Entries.Should().NotContainKey(TestHarnessConstants.DefaultResourceKey);
    }

    [Test]
    public async Task ClearingCoreAgentDefaultWorksWhenAgentOrchestrationIsDisabled()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var service = host.Services.GetRequiredService<DefaultResourceSetService>();

        service.IsValidKey("agent").Should().BeTrue();
        await service.SetKeyForChannelAsync(seeded.Channel.Id, "agent", seeded.Agent.Id);
        await service.ClearKeyForChannelAsync(seeded.Channel.Id, "agent");

        var result = await service.GetForChannelAsync(seeded.Channel.Id);
        result!.Entries.Should().NotContainKey("agent");
    }

    [Test]
    public async Task SetClearSetSameDefaultKeySurvivesContextReload()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var service = host.Services.GetRequiredService<DefaultResourceSetService>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await service.SetKeyForChannelAsync(
            seeded.Channel.Id,
            TestHarnessConstants.DefaultResourceKey,
            first);
        await service.ClearKeyForChannelAsync(
            seeded.Channel.Id,
            TestHarnessConstants.DefaultResourceKey);
        host.Db.ChangeTracker.Clear();
        await service.SetKeyForChannelAsync(
            seeded.Channel.Id,
            TestHarnessConstants.DefaultResourceKey,
            second);

        var result = await service.GetForChannelAsync(seeded.Channel.Id);
        result!.Entries[TestHarnessConstants.DefaultResourceKey].Should().Be(second);
    }

    [Test]
    public async Task UnknownDefaultKeyClearIsIdempotent()
    {
        await using var host = ChatHarnessHost.Create();
        var seeded = await host.SeedChatAsync(TestHarnessConstants.PlainProviderKey);
        var service = host.Services.GetRequiredService<DefaultResourceSetService>();

        await service.ClearKeyForChannelAsync(seeded.Channel.Id, "unknown_key");
        await service.ClearKeyForChannelAsync(seeded.Channel.Id, "unknown_key");

        var result = await service.GetForChannelAsync(seeded.Channel.Id);
        result!.Entries.Should().BeEmpty();
    }

    [Test]
    public void ModuleDefinedDefaultResourceKeyCannotShadowCoreAgentKey()
    {
        var registry = new ModuleRegistry();

        registry.Register(new ShadowCoreAgentDefaultModule());

        registry.IsRegisteredDefaultResourceKey("agent").Should().BeTrue();
        registry.GetDescriptorByDefaultResourceKey("agent").Should().BeNull();
        registry.GetAllDefaultResourceKeys().Count(k => string.Equals(k, "agent", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    private sealed class ShadowCoreAgentDefaultModule : ISharpClawModule
    {
        public string Id => "shadow_core_agent_default";
        public string DisplayName => "Shadow Core Agent Default";
        public string ToolPrefix => "shadow";

        public void ConfigureServices(IServiceCollection services) { }

        public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

        public Task<string> ExecuteToolAsync(
            string toolName,
            JsonElement parameters,
            AgentJobContext job,
            IServiceProvider scopedServices,
            CancellationToken ct) =>
            Task.FromResult("");

        public IReadOnlyList<ModuleResourceTypeDescriptor> GetResourceTypeDescriptors() =>
        [
            new(
                "Shadow.Agent",
                "ShadowAgent",
                "UseShadowAgentAsync",
                static (_, _) => Task.FromResult(new List<Guid>()),
                DefaultResourceKey: "agent")
        ];
    }
}
