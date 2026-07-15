using SharpClaw.Contracts.DTOs.Channels;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.Enums;
using SharpClaw.Core.Tasks.Runtime;

namespace SharpClaw.Tests.Core;

[TestFixture]
public sealed class TaskHostBridgeWorkflowEngineTests
{
    [Test]
    public async Task ChatAsync_BuildsTaskChatRequestAndAppendsCanonicalLog()
    {
        var engine = new TaskHostBridgeWorkflowEngine();
        var host = new RecordingHost();
        var instanceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        host.InstanceChannelIds[instanceId] = channelId;
        host.ChatContent = "answer";

        var response = await engine.ChatAsync(
            instanceId,
            "BridgeTask",
            "hello",
            agentId,
            host,
            CancellationToken.None);

        response.Should().Be("answer");
        host.LastChatChannelId.Should().Be(channelId);
        host.LastChatThreadId.Should().BeNull();
        host.LastChatRequest.Should().NotBeNull();
        host.LastChatRequest!.Message.Should().Be("hello");
        host.LastChatRequest.AgentId.Should().Be(agentId);
        host.LastChatRequest.ClientType.Should().Be(WellKnownClientKeys.Api);
        host.LastChatRequest.TaskContext.Should().Be(
            new TaskChatContext(instanceId, "BridgeTask"));
        host.Logs.Should().ContainSingle().Which.Should().Be(
            (instanceId, "Chat \u2192 6 chars"));
    }

    [Test]
    public async Task CreateChannelAsync_UpdatesExistingChannelAdoptsInstanceAndLogs()
    {
        var engine = new TaskHostBridgeWorkflowEngine();
        var host = new RecordingHost();
        var instanceId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid();
        var channel = new ChannelState
        {
            Id = Guid.NewGuid(),
            Title = "old",
            CustomId = "task.channel"
        };
        var instance = new TaskInstanceState
        {
            Id = instanceId,
            ContextId = contextId
        };
        host.ChannelsByCustomId[channel.CustomId] = channel;
        host.Instances[instanceId] = instance;

        var channelId = await engine.CreateChannelAsync(
            instanceId,
            "Task Channel",
            agentId,
            "task.channel",
            host,
            CancellationToken.None);

        channelId.Should().Be(channel.Id);
        channel.Title.Should().Be("Task Channel");
        channel.AgentId.Should().Be(agentId);
        channel.CustomId.Should().Be("task.channel");
        channel.AgentContextId.Should().Be(contextId);
        instance.ChannelId.Should().Be(channel.Id);
        host.SaveCount.Should().Be(2);
        host.Invalidations.Should().Contain(
            (TaskHostBridgeInvalidationTarget.Channel, channel.Id));
        host.Logs.Should().Contain(
            (instanceId, $"CreateChannel 'Task Channel' \u2192 {channel.Id}"));
    }

    [Test]
    public async Task SetRolePermissionsAsync_ParsesJsonAndReconcilesPermissionSet()
    {
        var engine = new TaskHostBridgeWorkflowEngine();
        var host = new RecordingHost();
        var role = new RoleState
        {
            Id = Guid.NewGuid(),
            Name = "Worker",
            PermissionSet = new PermissionSetState()
        };
        host.Roles[role.Id] = role;
        var requestJson =
            """{"globalFlags":{"CanUseBridge":5}}""";

        await engine.SetRolePermissionsAsync(
            role.Id,
            requestJson,
            host,
            CancellationToken.None);

        role.PermissionSet.GlobalFlags.Should().ContainSingle();
        role.PermissionSet.GlobalFlags.Single().FlagKey.Should().Be(
            "CanUseBridge");
        role.PermissionSet.GlobalFlags.Single().Clearance.Should().Be(
            PermissionClearance.Independent);
        host.SaveCount.Should().Be(1);
        host.Invalidations.Should().Contain(
            (TaskHostBridgeInvalidationTarget.Permission, role.Id));
    }

    private sealed class RecordingHost : ITaskHostBridgeWorkflowHost
    {
        public Dictionary<Guid, Guid?> InstanceChannelIds { get; } = [];
        public Dictionary<Guid, TaskInstanceState> Instances { get; } = [];
        public Dictionary<string, ChannelState> ChannelsByCustomId { get; } = [];
        public Dictionary<string, ChannelState> ChannelsByTitle { get; } = [];
        public Dictionary<Guid, RoleState> Roles { get; } = [];
        public List<(Guid InstanceId, string Message)> Logs { get; } = [];
        public List<(TaskHostBridgeInvalidationTarget Target, Guid? EntityId)>
            Invalidations { get; } = [];
        public string ChatContent { get; set; } = "";
        public Guid? LastChatChannelId { get; private set; }
        public Guid? LastChatThreadId { get; private set; }
        public ChatRequest? LastChatRequest { get; private set; }
        public int SaveCount { get; private set; }

        public Task<Guid?> LoadInstanceChannelIdAsync(
            Guid instanceId,
            CancellationToken ct)
        {
            return Task.FromResult(
                InstanceChannelIds.GetValueOrDefault(instanceId));
        }

        public Task<Guid?> LoadInstanceContextIdAsync(
            Guid instanceId,
            CancellationToken ct)
        {
            return Task.FromResult(
                Instances.TryGetValue(instanceId, out var instance)
                    ? instance.ContextId
                    : null);
        }

        public string? LoadTaskDefinitionSourceText(Guid instanceId) => null;

        public Task<ChatResponse> SendChatAsync(
            Guid channelId,
            ChatRequest request,
            Guid? threadId,
            CancellationToken ct)
        {
            LastChatChannelId = channelId;
            LastChatRequest = request;
            LastChatThreadId = threadId;

            return Task.FromResult(new ChatResponse(
                new ChatMessageResponse("user", request.Message, DateTimeOffset.UtcNow),
                new ChatMessageResponse("assistant", ChatContent, DateTimeOffset.UtcNow)));
        }

        public async IAsyncEnumerable<ChatStreamEvent> SendChatStreamAsync(
            Guid channelId,
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            LastChatChannelId = channelId;
            LastChatRequest = request;
            await Task.Yield();
            yield return ChatStreamEvent.TextDelta(ChatContent);
        }

        public Task AppendTaskLogAsync(
            Guid instanceId,
            string message,
            CancellationToken ct)
        {
            Logs.Add((instanceId, message));
            return Task.CompletedTask;
        }

        public Task<Guid?> FindIdAsync(
            TaskHostBridgeLookupKind kind,
            string search,
            CancellationToken ct)
        {
            return Task.FromResult<Guid?>(null);
        }

        public Task<AgentState?> LoadLatestAgentByCustomIdAsync(
            string customId,
            CancellationToken ct)
        {
            return Task.FromResult<AgentState?>(null);
        }

        public void TrackAgent(AgentState agent)
        {
        }

        public Task<ChannelState?> LoadChannelWithAllowedAgentsAsync(
            Guid channelId,
            CancellationToken ct)
        {
            var channel = ChannelsByCustomId.Values
                .Concat(ChannelsByTitle.Values)
                .FirstOrDefault(candidate => candidate.Id == channelId);
            return Task.FromResult(channel);
        }

        public void TrackThread(ChatThreadState thread)
        {
        }

        public Task<RoleState?> LoadRoleByNameAsync(
            string roleName,
            CancellationToken ct)
        {
            return Task.FromResult(
                Roles.Values.FirstOrDefault(role => role.Name == roleName));
        }

        public Task<Guid> CreateRoleAsync(string roleName, CancellationToken ct)
        {
            var role = new RoleState { Id = Guid.NewGuid(), Name = roleName };
            Roles[role.Id] = role;
            return Task.FromResult(role.Id);
        }

        public Task<RoleState?> LoadRoleWithPermissionSetAsync(
            Guid roleId,
            CancellationToken ct)
        {
            return Task.FromResult(Roles.GetValueOrDefault(roleId));
        }

        public Task<PermissionSetState> EnsureRolePermissionSetAsync(
            RoleState role,
            CancellationToken ct)
        {
            role.PermissionSet ??= new PermissionSetState();
            return Task.FromResult(role.PermissionSet);
        }

        public Task LoadPermissionSetCollectionsAsync(
            PermissionSetState permissionSet,
            CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<AgentState?> LoadAgentAsync(Guid agentId, CancellationToken ct)
        {
            return Task.FromResult<AgentState?>(null);
        }

        public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken ct)
        {
            return Task.FromResult(Roles.ContainsKey(roleId));
        }

        public Task<ChannelState?> LoadChannelByCustomIdAsync(
            string customId,
            CancellationToken ct)
        {
            return Task.FromResult(
                ChannelsByCustomId.GetValueOrDefault(customId));
        }

        public Task<ChannelState?> LoadChannelByTitleAsync(
            string title,
            CancellationToken ct)
        {
            return Task.FromResult(ChannelsByTitle.GetValueOrDefault(title));
        }

        public Task<Guid> CreateChannelAsync(
            CreateChannelRequest request,
            CancellationToken ct)
        {
            var title = request.Title ?? string.Empty;
            var channel = new ChannelState
            {
                Id = Guid.NewGuid(),
                Title = title,
                AgentId = request.AgentId,
                CustomId = request.CustomId,
                AgentContextId = request.ContextId
            };
            if (request.CustomId is null)
                ChannelsByTitle[title] = channel;
            else
                ChannelsByCustomId[request.CustomId] = channel;
            return Task.FromResult(channel.Id);
        }

        public Task<bool> TryAdoptInstanceChannelAsync(
            Guid instanceId,
            Guid channelId,
            CancellationToken ct)
        {
            if (!Instances.TryGetValue(instanceId, out var instance)
                || instance.ChannelId is not null)
            {
                return Task.FromResult(false);
            }

            instance.ChannelId = channelId;
            SaveCount++;
            return Task.FromResult(true);
        }

        public Task SaveAsync(CancellationToken ct)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public void Invalidate(
            TaskHostBridgeInvalidationTarget target,
            Guid? entityId = null)
        {
            Invalidations.Add((target, entityId));
        }
    }
}
