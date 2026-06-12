using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Modules.ModuleDev;
using SharpClaw.Modules.ModuleDev.Services;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class ModuleDevAgentWorkflowTests
{
    private string _externalModulesDir = null!;

    [SetUp]
    public void SetUp()
    {
        _externalModulesDir = Path.Combine(
            Path.GetTempPath(),
            "SharpClawModuleDevAgentWorkflowTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_externalModulesDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_externalModulesDir))
            Directory.Delete(_externalModulesDir, recursive: true);
    }

    [Test]
    public void GetToolDefinitions_ExposeAgentWorkflowTools()
    {
        var module = new ModuleDevModule();

        var names = module.GetToolDefinitions().Select(tool => tool.Name);

        names.Should().Contain("get_sdk_reference");
        names.Should().Contain("apply_module_files");
        names.Should().Contain("apply_task_source");
        names.Should().Contain("record_conversation_steering");
        names.Should().Contain("list_conversation_steering");
    }

    [Test]
    public async Task GetSdkReference_ReturnsRuntimeReferenceForAgents()
    {
        var module = new ModuleDevModule();
        await using var provider = CreateProvider(new RecordingLifecycle(_externalModulesDir));
        using var parameters = JsonDocument.Parse("""{"topic":"javascript"}""");

        var result = await module.ExecuteToolAsync(
            "get_sdk_reference",
            parameters.RootElement,
            Job(),
            provider,
            CancellationToken.None);

        result.Should().Contain("@sharpclaw/module-host");
        result.Should().Contain("addConversationSteering");
    }

    [Test]
    public async Task ApplyModuleFiles_WritesLoadsAndSteersWorkflowResult()
    {
        var lifecycle = new RecordingLifecycle(_externalModulesDir);
        var steering = new RecordingConversationSteering();
        var module = new ModuleDevModule();
        await using var provider = CreateProvider(lifecycle, steering: steering);
        var channelId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var threadId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using var parameters = JsonDocument.Parse($$"""
            {
              "module_id": "sample_node",
              "runtime": "node",
              "load": true,
              "files": [
                {
                  "relative_path": "module.json",
                  "content": "{\"id\":\"sample_node\",\"displayName\":\"Sample Node\",\"toolPrefix\":\"sn\",\"runtime\":\"node\",\"entrypoint\":\"module.mjs\",\"entryAssembly\":\"\"}"
                },
                {
                  "relative_path": "module.mjs",
                  "content": "export {};"
                }
              ],
              "conversation": {
                "channel_id": "{{channelId}}",
                "thread_id": "{{threadId}}"
              }
            }
            """);

        var result = await module.ExecuteToolAsync(
            "apply_module_files",
            parameters.RootElement,
            Job(channelId),
            provider,
            CancellationToken.None);

        using var payload = JsonDocument.Parse(result);
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        File.Exists(Path.Combine(_externalModulesDir, "sample_node", "module.mjs")).Should().BeTrue();
        lifecycle.LoadedDir.Should().Be(Path.Combine(_externalModulesDir, "sample_node"));
        steering.Requests.Should().ContainSingle();
        steering.Requests[0].ChannelId.Should().Be(channelId);
        steering.Requests[0].ThreadId.Should().Be(threadId);
        steering.Requests[0].Category.Should().Be("module_workflow");
        steering.Requests[0].Summary.Should().Contain("hot-loaded");
    }

    [Test]
    public async Task ApplyTaskSource_WhenValidationFails_DoesNotSaveAndSteersDiagnostics()
    {
        var authoring = new RecordingTaskAuthoring(isValid: false);
        var steering = new RecordingConversationSteering();
        var module = new ModuleDevModule();
        await using var provider = CreateProvider(
            new RecordingLifecycle(_externalModulesDir),
            authoring,
            steering);
        var channelId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        using var parameters = JsonDocument.Parse($$"""
            {
              "source_text": "bad task source",
              "conversation": {
                "channel_id": "{{channelId}}"
              }
            }
            """);

        var result = await module.ExecuteToolAsync(
            "apply_task_source",
            parameters.RootElement,
            Job(channelId),
            provider,
            CancellationToken.None);

        using var payload = JsonDocument.Parse(result);
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        authoring.CreatedSource.Should().BeNull();
        authoring.Updated.Should().BeNull();
        steering.Requests.Should().ContainSingle();
        steering.Requests[0].Category.Should().Be("task_validation");
        steering.Requests[0].Details.Should().Contain("MDK001");
    }

    [Test]
    public async Task ApplyTaskSource_WhenValid_CreatesTaskAndSteersSavedId()
    {
        var authoring = new RecordingTaskAuthoring(isValid: true);
        var steering = new RecordingConversationSteering();
        var module = new ModuleDevModule();
        await using var provider = CreateProvider(
            new RecordingLifecycle(_externalModulesDir),
            authoring,
            steering);
        var channelId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        using var parameters = JsonDocument.Parse($$"""
            {
              "source_text": "good task source",
              "conversation": {
                "channel_id": "{{channelId}}"
              }
            }
            """);

        var result = await module.ExecuteToolAsync(
            "apply_task_source",
            parameters.RootElement,
            Job(channelId),
            provider,
            CancellationToken.None);

        using var payload = JsonDocument.Parse(result);
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        authoring.CreatedSource.Should().Be("good task source");
        steering.Requests.Should().ContainSingle();
        steering.Requests[0].Category.Should().Be("task_workflow");
        steering.Requests[0].Details.Should().Contain(authoring.TaskId.ToString());
    }

    private ServiceProvider CreateProvider(
        RecordingLifecycle lifecycle,
        RecordingTaskAuthoring? authoring = null,
        RecordingConversationSteering? steering = null)
    {
        var services = new ServiceCollection();
        new ModuleDevModule().ConfigureServices(services);
        services.AddSingleton<IModuleLifecycleManager>(lifecycle);
        services.AddSingleton<IModuleInfoProvider>(new EmptyModuleInfoProvider());
        services.AddSingleton<ITaskAuthoring>(authoring ?? new RecordingTaskAuthoring(isValid: true));
        services.AddSingleton<IConversationSteering>(steering ?? new RecordingConversationSteering());
        return services.BuildServiceProvider();
    }

    private static AgentJobContext Job(Guid? channelId = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            channelId ?? Guid.Empty,
            ResourceId: null,
            ActionKey: "mdk_test");

    private sealed class RecordingLifecycle(string externalModulesDir) : IModuleLifecycleManager
    {
        public string ExternalModulesDir { get; } = externalModulesDir;
        public string? LoadedDir { get; private set; }
        public string? ReloadedId { get; private set; }

        public bool IsModuleRegistered(string moduleId) => false;
        public bool IsToolPrefixRegistered(string toolPrefix) => false;
        public (ISharpClawModule Module, string ToolName)? FindToolByName(string toolName) => null;

        public Task<ModuleStateResponse> LoadExternalAsync(
            string moduleDir,
            IServiceProvider hostServices,
            CancellationToken ct = default)
        {
            LoadedDir = moduleDir;
            return Task.FromResult(State(Path.GetFileName(moduleDir)));
        }

        public Task UnloadExternalAsync(string moduleId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<ModuleStateResponse> ReloadExternalAsync(
            string moduleId,
            IServiceProvider hostServices,
            CancellationToken ct = default)
        {
            ReloadedId = moduleId;
            return Task.FromResult(State(moduleId));
        }

        private static ModuleStateResponse State(string moduleId) =>
            new(
                moduleId,
                "Loaded Module",
                "lm",
                true,
                "0.1.0-beta",
                true,
                true,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
    }

    private sealed class EmptyModuleInfoProvider : IModuleInfoProvider
    {
        public IReadOnlyList<ModuleInfo> GetAllModules() => [];
    }

    private sealed class RecordingTaskAuthoring(bool isValid) : ITaskAuthoring
    {
        public Guid TaskId { get; } = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        public string? CreatedSource { get; private set; }
        public (Guid Id, string? SourceText, bool? IsActive)? Updated { get; private set; }

        public TaskValidationResponse ValidateDefinition(string sourceText) =>
            isValid
                ? new TaskValidationResponse(true, [])
                : new TaskValidationResponse(
                    false,
                    [new TaskDiagnosticResponse("Error", "MDK001", "Bad task source.", 1, 1)]);

        public Task<TaskDefinitionResponse> CreateDefinitionAsync(
            CreateTaskDefinitionRequest request,
            CancellationToken ct = default)
        {
            CreatedSource = request.SourceText;
            return Task.FromResult(Response(TaskId));
        }

        public Task<TaskDefinitionResponse?> GetDefinitionAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<TaskDefinitionResponse?>(id == TaskId ? Response(id) : null);

        public Task<IReadOnlyList<TaskDefinitionResponse>> ListDefinitionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TaskDefinitionResponse>>([Response(TaskId)]);

        public Task<TaskDefinitionResponse?> UpdateDefinitionAsync(
            Guid id,
            UpdateTaskDefinitionRequest request,
            CancellationToken ct = default)
        {
            Updated = (id, request.SourceText, request.IsActive);
            return Task.FromResult<TaskDefinitionResponse?>(Response(id));
        }

        public Task<bool> DeleteDefinitionAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(true);

        private static TaskDefinitionResponse Response(Guid id) =>
            new(
                id,
                "Generated Task",
                "Created by workflow",
                null,
                true,
                [],
                [],
                [],
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
    }

    private sealed class RecordingConversationSteering : IConversationSteering
    {
        public List<ConversationSteeringRequest> Requests { get; } = [];

        public Task<ConversationSteeringResponse> AddAsync(
            ConversationSteeringRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ConversationSteeringResponse(
                Guid.NewGuid(),
                request.ChannelId,
                request.ThreadId,
                request.Summary,
                DateTimeOffset.UnixEpoch,
                request.Source,
                request.Category));
        }

        public Task<IReadOnlyList<ConversationSteeringResponse>> ListAsync(
            Guid channelId,
            Guid? threadId = null,
            int limit = 20,
            CancellationToken ct = default)
        {
            IReadOnlyList<ConversationSteeringResponse> rows = Requests
                .Where(request => request.ChannelId == channelId && request.ThreadId == threadId)
                .Take(limit)
                .Select(request => new ConversationSteeringResponse(
                    Guid.NewGuid(),
                    request.ChannelId,
                    request.ThreadId,
                    request.Summary,
                    DateTimeOffset.UnixEpoch,
                    request.Source,
                    request.Category))
                .ToList();
            return Task.FromResult(rows);
        }
    }
}
