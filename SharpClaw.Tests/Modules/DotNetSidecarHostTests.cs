using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Application.Core.Modules.Foreign;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Tests.ExternalModule;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class DotNetSidecarHostTests
{
    [Test]
    public void ModuleManifestRuntimeInfoReadsDotNetSidecarPackageMetadata()
    {
        var json = SidecarManifestJson("SharpClaw.Tests.ExternalModule.dll");
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, SecureJsonOptions.Manifest)!;
        var runtimeInfo = ModuleManifestRuntimeInfo.FromJson(json);

        manifest.Id.Should().Be(DotNetSidecarFixtureModule.ModuleId);
        runtimeInfo.Runtime.Should().Be(ModuleManifestRuntimeInfo.DotNet);
        runtimeInfo.ModuleType.Should().Be(typeof(DotNetSidecarFixtureModule).FullName);
        runtimeInfo.IsSidecarHostMode.Should().BeTrue();
    }

    [Test]
    public async Task NuGetPackageCanDeclareDotNetSidecarModuleType()
    {
        using var workspace = TestWorkspace.Create();
        const string packageId = "SharpClaw.Tests.DotNetSidecar.Package";
        const string version = "1.0.0";
        Directory.CreateDirectory(workspace.PackageSourceDir);
        CreateSidecarPackage(workspace.PackageSourceDir, packageId, version);

        var moduleDir = await NuGetModulePackageResolver.ResolveAsync(
            new NuGetModulePackageReference(packageId, version, workspace.PackageSourceDir),
            workspace.PackageCacheDir);
        var json = await File.ReadAllTextAsync(Path.Combine(moduleDir, "module.json"));
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, SecureJsonOptions.Manifest)!;
        var runtimeInfo = ModuleManifestRuntimeInfo.FromJson(json);

        manifest.Id.Should().Be(DotNetSidecarFixtureModule.ModuleId);
        runtimeInfo.IsSidecarHostMode.Should().BeTrue();
        runtimeInfo.ModuleType.Should().Be(typeof(DotNetSidecarFixtureModule).FullName);
        File.Exists(Path.Combine(moduleDir, manifest.EntryAssembly)).Should().BeTrue();
    }

    [Test]
    public async Task DotNetSidecarHostAdaptsSharpClawModuleToForeignProtocol()
    {
        using var workspace = TestWorkspace.Create();
        CopyFixtureModulePayload(workspace.ModuleDir);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.ModuleDir, "module.json"),
            SidecarManifestJson("SharpClaw.Tests.ExternalModule.dll"));

        var manifest = JsonSerializer.Deserialize<ModuleManifest>(
            await File.ReadAllTextAsync(Path.Combine(workspace.ModuleDir, "module.json")),
            SecureJsonOptions.Manifest)!;
        var runtimeInfo = ModuleManifestRuntimeInfo.FromJson(
            await File.ReadAllTextAsync(Path.Combine(workspace.ModuleDir, "module.json")));
        var taskContextRegistry = new ForeignModuleTaskContextRegistry();
        var hostAgentBridge = new RecordingHostAgentBridge();
        await using var hostServices = new ServiceCollection()
            .AddSingleton<IModuleConfigStore, RecordingConfigStore>()
            .AddSingleton(taskContextRegistry)
            .AddSingleton<IHostAgentBridge>(hostAgentBridge)
            .BuildServiceProvider();
        await using var foreignHost = await ForeignModuleHost.StartAsync(
            manifest,
            runtimeInfo,
            CreateLaunchOptions(workspace, hostServices));

        foreignHost.Handshake.Runtime.Should().Be(ModuleManifestRuntimeInfo.DotNet);
        foreignHost.Endpoints.Should().Contain(endpoint =>
            endpoint.Method == "GET"
            && endpoint.RoutePattern == "/modules/dotnet-sidecar/ping");
        foreignHost.Module.GetToolDefinitions()
            .Should()
            .ContainSingle(tool => tool.Name == DotNetSidecarFixtureModule.JobTool);
        foreignHost.Module.GetInlineToolDefinitions()
            .Should()
            .ContainSingle(tool => tool.Name == DotNetSidecarFixtureModule.InlineTool);
        foreignHost.Handshake.Capabilities.Should().Contain("taskRuntime");

        using var payload = JsonDocument.Parse("""{"value":"hello"}""");
        var result = await foreignHost.Module.ExecuteToolAsync(
            DotNetSidecarFixtureModule.JobTool,
            payload.RootElement,
            new AgentJobContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, DotNetSidecarFixtureModule.JobTool),
            foreignHost.Services,
            CancellationToken.None);
        result.Should().Be("dotnet sidecar hello");

        var tag = foreignHost.Module.GetHeaderTags()!.Single();
        (await tag.Resolve(foreignHost.Services, CancellationToken.None)).Should().Be("hello");

        using var response = await foreignHost.SendEndpointRequestAsync(
            new HttpRequestMessage(HttpMethod.Get, "/modules/dotnet-sidecar/ping"),
            CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("dotnet sidecar pong");

        var parserAware = foreignHost.Module.Should().BeAssignableTo<ITaskParserAware>().Which;
        parserAware.ParserExtension.StepKeyMappings["DotNetSidecarStep"].StepKey
            .Should()
            .Be(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey);
        parserAware.ParserExtension.EventTriggerMappings["OnDotNetSidecar"].TriggerKey
            .Should()
            .Be(DotNetSidecarFixtureTriggerSource.TriggerKeyValue);

        var triggerContext = new TestTriggerAttributeContext(
            "DotNetSidecarTrigger",
            9,
            namedStringArgs: new Dictionary<string, string?> { ["Name"] = "fixture-trigger" });
        var trigger = parserAware.ParserExtension
            .TriggerAttributeHandlers["DotNetSidecarTrigger"]
            .Handle(triggerContext);
        trigger.Should().NotBeNull();
        trigger!.TriggerKey.Should().Be(DotNetSidecarFixtureTriggerSource.TriggerKeyValue);
        trigger.Parameters["name"].Should().Be("fixture-trigger");

        var taskServices = new ServiceCollection();
        foreignHost.Module.ConfigureServices(taskServices);
        await using var taskProvider = taskServices.BuildServiceProvider();

        var descriptorProvider = taskProvider.GetServices<ITaskStepDescriptorProvider>()
            .Should()
            .ContainSingle()
            .Subject;
        descriptorProvider.Descriptors.Should().ContainSingle(descriptor =>
            descriptor.StepKey == DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey);

        var executor = taskProvider.GetServices<ITaskStepExecutorExtension>()
            .Should()
            .ContainSingle()
            .Subject;
        var executionContext = new TestTaskStepExecutionContext(hostServices);
        (await executor.ExecuteAsync(
            DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey,
            executionContext,
            ["arg"],
            "expression",
            "stepResult")).Should().BeTrue();
        executionContext.Variables["dotnetSidecarStep"].Should().Be("expression");
        executionContext.Variables["stepResult"].Should().Be("dotnet-sidecar-step-result");
        executionContext.Logs.Should().Contain("dotnet sidecar step log");
        executionContext.Outputs.Should().Contain("""{"dotnetSidecar":true}""");

        var invocationResult = await ((ITaskStepInvocationExecutor)executor).ExecuteInvocationAsync(
            new TestTaskStepInvocation(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey)
            {
                RawExpression = "raw expression",
                ResultVariable = "invocationResult",
            },
            executionContext);
        invocationResult.Should().Be(TaskStepResult.Continue);
        executionContext.Variables["dotnetSidecarInvocation"].Should().Be("raw expression");
        executionContext.Variables["invocationResult"].Should().Be("dotnet-sidecar-invocation-result");

        var nestedResult = await ((ITaskStepInvocationExecutor)executor).ExecuteInvocationAsync(
            new TestTaskStepInvocation(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey)
            {
                RawExpression = "run-nested",
                Body =
                [
                    new TestTaskStepInvocation("synthetic.parent.nested"),
                ],
            },
            executionContext);
        nestedResult.Should().Be(TaskStepResult.Continue);
        executionContext.NestedStepKeys.Should().ContainSingle().Which.Should().Be("synthetic.parent.nested");
        executionContext.Variables["dotnetSidecarNestedResult"].Should().Be("Continue");
        executionContext.Variables["parentNestedExecuted"].Should().Be("true");

        await ((ITaskStepInvocationExecutor)executor).ExecuteInvocationAsync(
            new TestTaskStepInvocation(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey)
            {
                RawExpression = "bridge-find-model",
            },
            executionContext);
        executionContext.Variables["dotnetSidecarBridgeModelId"]
            .Should()
            .Be(hostAgentBridge.ModelId.ToString());
        hostAgentBridge.FindModelSearches.Should().ContainSingle().Which.Should().Be("sidecar-model");

        executionContext.EventHandlers.Add(new TestTaskEventHandler(
            DotNetSidecarFixtureTaskStepDescriptorProvider.ParentHandlerTriggerKey,
            "evt",
            () => executionContext.Variables["parentHandlerExecuted"] = "true"));
        await ((ITaskStepInvocationExecutor)executor).ExecuteInvocationAsync(
            new TestTaskStepInvocation(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey)
            {
                RawExpression = "execute-parent-handler",
            },
            executionContext);
        executionContext.Variables["dotnetSidecarParentHandlerExecuted"].Should().Be("true");
        executionContext.Variables["parentHandlerExecuted"].Should().Be("true");

        await ((ITaskStepInvocationExecutor)executor).ExecuteInvocationAsync(
            new TestTaskStepInvocation(DotNetSidecarFixtureTaskStepDescriptorProvider.StepKey)
            {
                RawExpression = "register-handler",
                Body =
                [
                    new TestTaskStepInvocation("synthetic.registered.body"),
                ],
            },
            executionContext);
        executionContext.RegisteredHandlers
            .Should()
            .ContainSingle(handler =>
                handler.ModuleTriggerKey == DotNetSidecarFixtureTaskStepDescriptorProvider.RegisteredHandlerTriggerKey
                && handler.Body.Single().StepKey == "synthetic.registered.body");

        var triggerSource = taskProvider.GetServices<ITaskTriggerSource>()
            .Should()
            .ContainSingle()
            .Subject;
        triggerSource.TriggerKeys.Should().Equal(DotNetSidecarFixtureTriggerSource.TriggerKeyValue);
        triggerSource.GetBindingValue(trigger).Should().Be("fixture-trigger");
        triggerSource.GetBindingFilter(trigger).Should().Be("dotnet-filter");
        (await triggerSource.SyncBindingsAsync(
            new TaskDefinitionDescriptor(Guid.NewGuid(), "fixture"),
            [trigger],
            CancellationToken.None)).Should().BeTrue();
        await triggerSource.RemoveBindingsAsync(Guid.NewGuid(), CancellationToken.None);
        await triggerSource.StopAsync();

        var sideEffect = taskProvider.GetServices<ITaskTriggerBindingSideEffect>()
            .Should()
            .ContainSingle()
            .Subject;
        sideEffect.TriggerKey.Should().Be(DotNetSidecarFixtureTriggerSource.TriggerKeyValue);
        var binding = new TaskTriggerBindingDescriptor(
            Guid.NewGuid(),
            DotNetSidecarFixtureTriggerSource.TriggerKeyValue,
            "fixture-trigger",
            "dotnet-filter");
        await sideEffect.OnBindingCreatedAsync(
            new TaskDefinitionDescriptor(Guid.NewGuid(), "fixture"),
            trigger,
            binding,
            CancellationToken.None);
        await sideEffect.OnBindingRemovedAsync(binding, CancellationToken.None);

        var metric = taskProvider.GetServices<ITaskMetricProvider>()
            .Should()
            .ContainSingle()
            .Subject;
        metric.MetricName.Should().Be(DotNetSidecarFixtureMetricProvider.MetricNameValue);
        (await metric.GetValueAsync(CancellationToken.None)).Should().Be(13.5);

        var eventSink = taskProvider.GetServices<ISharpClawEventSink>()
            .Should()
            .ContainSingle()
            .Subject;
        eventSink.SubscribedEvents.Should().Be(SharpClawEventType.AllModuleEvents);
        await eventSink.OnEventAsync(
            new SharpClawEvent(SharpClawEventType.ModuleEnabled, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private static ForeignModuleHostLaunchOptions CreateLaunchOptions(
        TestWorkspace workspace,
        IServiceProvider hostServices)
    {
        var hostPath = ResolveDotNetSidecarHostPath();
        return new ForeignModuleHostLaunchOptions
        {
            ExecutablePath = "dotnet",
            Arguments = [hostPath],
            WorkingDirectory = Path.GetDirectoryName(hostPath),
            ModuleDirectory = workspace.ModuleDir,
            ModuleDataDirectory = workspace.DataDir,
            ControlAddress = new Uri($"http://127.0.0.1:{GetFreeTcpPort()}"),
            ControlToken = "dotnet-sidecar-token",
            StartupTimeout = TimeSpan.FromSeconds(10),
            ShutdownTimeout = TimeSpan.FromSeconds(3),
            HostVersion = "0.1.0-beta",
            HostServices = hostServices,
        };
    }

    private static string SidecarManifestJson(string entryAssembly) =>
        $$"""
        {
          "id": "{{DotNetSidecarFixtureModule.ModuleId}}",
          "displayName": "Synthetic .NET Sidecar",
          "version": "1.0.0",
          "toolPrefix": "{{DotNetSidecarFixtureModule.ToolPrefixValue}}",
          "runtime": "dotnet",
          "hostMode": "sidecar",
          "entryAssembly": "{{entryAssembly}}",
          "moduleType": "{{typeof(DotNetSidecarFixtureModule).FullName}}",
          "minHostVersion": "0.0.0"
        }
        """;

    private static void CreateSidecarPackage(string packageSource, string packageId, string version)
    {
        var packagePath = Path.Combine(packageSource, $"{packageId}.{version}.nupkg");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteTextEntry(archive, "module.json", SidecarManifestJson("SharpClaw.Tests.ExternalModule.dll"));
        WriteTextEntry(
            archive,
            $"{packageId}.nuspec",
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{version}</version>
                <authors>SharpClaw.Tests</authors>
                <description>Synthetic SharpClaw .NET sidecar module package.</description>
              </metadata>
            </package>
            """);

        foreach (var file in FixturePayloadFiles())
            archive.CreateEntryFromFile(file, Path.GetFileName(file));
    }

    private static void CopyFixtureModulePayload(string moduleDir)
    {
        foreach (var file in FixturePayloadFiles())
            File.Copy(file, Path.Combine(moduleDir, Path.GetFileName(file)), overwrite: true);
    }

    private static IEnumerable<string> FixturePayloadFiles()
    {
        var sourceDir = Path.GetDirectoryName(typeof(DotNetSidecarFixtureModule).Assembly.Location)!;
        foreach (var file in Directory.GetFiles(sourceDir, "SharpClaw.Tests.ExternalModule.*"))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string ResolveDotNetSidecarHostPath()
    {
        var root = ResolveRepoRoot();
        var configuration = Directory.GetParent(TestContext.CurrentContext.TestDirectory)!.Name;
        var hostPath = Path.Combine(
            root,
            "SharpClaw.Modules.DotNetSidecarHost",
            "bin",
            configuration,
            "net10.0",
            "SharpClaw.Modules.DotNetSidecarHost.dll");

        File.Exists(hostPath).Should().BeTrue(
            $"shared .NET sidecar host must be built before tests run: '{hostPath}'");
        return hostPath;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Packages.props")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SharpClaw repository root.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private sealed class RecordingConfigStore : IModuleConfigStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            where T : IParsable<T>
        {
            var value = _values.GetValueOrDefault(key);
            return Task.FromResult(
                value is not null && T.TryParse(value, null, out var parsed)
                    ? parsed
                    : default);
        }

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            if (value is null)
                _values.Remove(key);
            else
                _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(_values, StringComparer.Ordinal));
    }

    private sealed class TestTaskStepExecutionContext(IServiceProvider services) : ITaskStepExecutionContext
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public Guid ChannelId { get; private set; } = Guid.NewGuid();
        public CancellationToken CancellationToken => CancellationToken.None;
        public IServiceProvider Services { get; } = services;
        public IDictionary<string, object?> Variables { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
        public List<ITaskEventHandler> EventHandlers { get; } = [];
        IReadOnlyList<ITaskEventHandler> ITaskStepExecutionContext.EventHandlers => EventHandlers;
        public List<string> Logs { get; } = [];
        public List<string?> Outputs { get; } = [];
        public List<string> NestedStepKeys { get; } = [];
        public List<(string ModuleTriggerKey, string? ParameterName, IReadOnlyList<ITaskStepInvocation> Body)> RegisteredHandlers { get; } = [];

        public string ResolveExpression(string expression) =>
            Variables.TryGetValue(expression, out var value)
                ? value?.ToString() ?? string.Empty
                : expression;

        public Task AppendLogAsync(string message)
        {
            Logs.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteOutputAsync(string? outputJson)
        {
            Outputs.Add(outputJson);
            return Task.CompletedTask;
        }

        public void SetChannelId(Guid channelId) => ChannelId = channelId;

        public Task<TaskStepResult> ExecuteStepsAsync(
            IReadOnlyList<ITaskStepInvocation> steps,
            CancellationToken cancellationToken)
        {
            NestedStepKeys.AddRange(steps.Select(step => step.StepKey));
            Variables["parentNestedExecuted"] = "true";
            return Task.FromResult(TaskStepResult.Continue);
        }

        public bool EvaluateCondition(string? expression) =>
            bool.TryParse(expression, out var value) && value;

        public void RegisterEventHandler(
            string moduleTriggerKey,
            string? parameterName,
            IReadOnlyList<ITaskStepInvocation> body)
        {
            RegisteredHandlers.Add((moduleTriggerKey, parameterName, body));
        }

        public Task WaitIfPausedAsync() => Task.CompletedTask;
    }

    private sealed class RecordingHostAgentBridge : IHostAgentBridge
    {
        public Guid ModelId { get; } = Guid.Parse("abababab-abab-abab-abab-abababababab");
        public List<string> FindModelSearches { get; } = [];

        public Task<string?> ChatAsync(
            Guid instanceId,
            string taskName,
            string message,
            Guid? agentId,
            CancellationToken ct) =>
            Task.FromResult<string?>("chat response");

        public Task<string> ChatStreamAsync(
            Guid instanceId,
            string taskName,
            string message,
            Guid? agentId,
            CancellationToken ct) =>
            Task.FromResult("stream response");

        public Task<string?> ChatToThreadAsync(
            Guid instanceId,
            string taskName,
            Guid threadId,
            string message,
            Guid? agentId,
            CancellationToken ct) =>
            Task.FromResult<string?>("thread response");

        public string ParseStructuredResponse(Guid instanceId, string text, string? typeName) => text;

        public Task<Guid?> FindModelAsync(string search, CancellationToken ct)
        {
            FindModelSearches.Add(search);
            return Task.FromResult<Guid?>(ModelId);
        }

        public Task<Guid?> FindProviderAsync(string search, CancellationToken ct) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid?> FindAgentAsync(string search, CancellationToken ct) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid?> FindRoleAsync(string search, CancellationToken ct) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid?> FindChannelAsync(string search, CancellationToken ct) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid> CreateAgentAsync(
            Guid instanceId,
            string name,
            Guid modelId,
            string? systemPrompt,
            string? customId,
            CancellationToken ct) =>
            Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateThreadAsync(
            Guid instanceId,
            Guid? channelId,
            string? threadName,
            CancellationToken ct) =>
            Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateRoleAsync(string roleName, CancellationToken ct) =>
            Task.FromResult(Guid.NewGuid());

        public Task SetRolePermissionsAsync(
            Guid roleId,
            string requestJson,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task AssignRoleAsync(
            Guid agentId,
            Guid roleId,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<Guid> CreateChannelAsync(
            Guid instanceId,
            string title,
            Guid agentId,
            string? customId,
            CancellationToken ct) =>
            Task.FromResult(Guid.NewGuid());

        public Task AddAllowedAgentAsync(
            Guid instanceId,
            Guid agentId,
            Guid? channelId,
            CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed record TestTaskEventHandler(
        string? ModuleTriggerKey,
        string? ParameterName,
        Action Action) : ITaskEventHandler
    {
        public Task ExecuteBodyAsync(CancellationToken ct)
        {
            Action();
            return Task.CompletedTask;
        }
    }

    private sealed record TestTaskStepInvocation(string StepKey) : ITaskStepInvocation
    {
        public string? VariableName { get; init; }
        public string? TypeName { get; init; }
        public string? ResultVariable { get; init; }
        public string? RawExpression { get; init; }
        public IReadOnlyList<string>? Arguments { get; init; }
        public string? ModuleTriggerKey { get; init; }
        public string? HandlerParameter { get; init; }
        public IReadOnlyList<ITaskStepInvocation>? Body { get; init; }
        public IReadOnlyList<ITaskStepInvocation>? ElseBody { get; init; }
    }

    private sealed class TestTriggerAttributeContext : TaskTriggerAttributeContext
    {
        private readonly IReadOnlyDictionary<string, string?> _namedStringArgs;

        public TestTriggerAttributeContext(
            string attributeName,
            int line,
            IReadOnlyDictionary<string, string?>? namedStringArgs = null)
        {
            AttributeName = attributeName;
            Line = line;
            _namedStringArgs = namedStringArgs ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        public override string AttributeName { get; }
        public override int Line { get; }
        public override int ArgumentCount => 0;
        public override string? GetStringArg(int index) => null;
        public override int? GetIntArg(int index) => null;
        public override string? GetNamedStringArg(string name) =>
            _namedStringArgs.TryGetValue(name, out var value) ? value : null;
        public override int? GetNamedIntArg(string name) => null;
        public override double? GetNamedDoubleArg(string name) => null;
        public override T? GetNamedEnumArg<T>(string name) where T : struct => null;
        public override string? GetRawArgText(int index) => null;
        public override void Report(
            TaskTriggerAttributeDiagnosticSeverity severity,
            string code,
            string message)
        {
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root)
        {
            Root = root;
            ModuleDir = Path.Combine(root, "module");
            DataDir = Path.Combine(root, "data");
            PackageSourceDir = Path.Combine(root, "packages");
            PackageCacheDir = Path.Combine(root, "package-cache");
            Directory.CreateDirectory(ModuleDir);
            Directory.CreateDirectory(DataDir);
        }

        public string Root { get; }
        public string ModuleDir { get; }
        public string DataDir { get; }
        public string PackageSourceDir { get; }
        public string PackageCacheDir { get; }

        public static TestWorkspace Create() =>
            new(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "dotnet-sidecar",
                Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
