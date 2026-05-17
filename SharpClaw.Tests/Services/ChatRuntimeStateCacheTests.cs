using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.Chat;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Providers;

namespace SharpClaw.Tests.Services;

[TestFixture]
public sealed class ChatRuntimeStateCacheTests
{
    [Test]
    public async Task ChatProcessingBridge_CachesContributorResultsWithinTtl()
    {
        var contributor = new CountingContributor();
        var bridge = new ChatProcessingBridge(
            [contributor],
            CreateCache(cacheSeconds: 60));

        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var firstTools = await bridge.GetExtraToolsAsync(agentId);
        var secondTools = await bridge.GetExtraToolsAsync(agentId);
        var firstThreads = await bridge.GetAccessibleThreadsAsync(agentId, channelId);
        var secondThreads = await bridge.GetAccessibleThreadsAsync(agentId, channelId);

        firstTools.Should().BeEquivalentTo(secondTools);
        firstThreads.Should().BeEquivalentTo(secondThreads);
        contributor.ToolCalls.Should().Be(1);
        contributor.ThreadCalls.Should().Be(1);
    }

    [Test]
    public async Task ChatProcessingBridge_CanDisableRuntimeStateCache()
    {
        var contributor = new CountingContributor();
        var bridge = new ChatProcessingBridge(
            [contributor],
            CreateCache(cacheSeconds: 0));

        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await bridge.GetExtraToolsAsync(agentId);
        await bridge.GetExtraToolsAsync(agentId);
        await bridge.GetAccessibleThreadsAsync(agentId, channelId);
        await bridge.GetAccessibleThreadsAsync(agentId, channelId);

        contributor.ToolCalls.Should().Be(2);
        contributor.ThreadCalls.Should().Be(2);
    }

    private static ChatRuntimeStateCache CreateCache(int cacheSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chat:RuntimeStateCacheSeconds"] = cacheSeconds.ToString()
            })
            .Build();

        return new ChatRuntimeStateCache(configuration);
    }

    private sealed class CountingContributor : IChatProcessingContributor
    {
        private readonly JsonElement _schema = JsonDocument.Parse("{}").RootElement.Clone();

        public int ToolCalls { get; private set; }
        public int ThreadCalls { get; private set; }

        public Task<IReadOnlyList<ChatToolDefinition>> GetExtraToolsAsync(
            Guid agentId, CancellationToken ct = default)
        {
            ToolCalls++;
            IReadOnlyList<ChatToolDefinition> tools =
            [
                new("test_tool", "Test tool.", _schema)
            ];
            return Task.FromResult(tools);
        }

        public Task<IReadOnlyList<ThreadSummary>> GetAccessibleThreadsAsync(
            Guid agentId, Guid currentChannelId, CancellationToken ct = default)
        {
            ThreadCalls++;
            IReadOnlyList<ThreadSummary> threads =
            [
                new(Guid.NewGuid(), "Thread", Guid.NewGuid(), "Channel")
            ];
            return Task.FromResult(threads);
        }
    }
}
