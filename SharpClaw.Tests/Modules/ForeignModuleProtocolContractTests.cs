using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Application.Core.Modules.Foreign;
using SharpClaw.Contracts.Modules;
using ModuleManifestRuntimeInfo = SharpClaw.Application.Core.Modules.ModuleManifestRuntimeInfo;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class ForeignModuleProtocolContractTests
{
    [Test]
    public async Task ForeignModuleExportsProtocolContractAndInvoker()
    {
        using var workspace = TestWorkspace.Create();
        await using var foreignHost = await ForeignModuleHost.StartAsync(
            Manifest(),
            RuntimeInfo(),
            CreateLaunchOptions(workspace));
        var registry = new ModuleRegistry();
        registry.Register(foreignHost.Module, foreignHost);
        using var parameters = JsonDocument.Parse("""{"path":"README.md"}""");

        var resolved = registry.ResolveProtocolContract("editor_bridge");
        var invoker = registry.ResolveProtocolContractInvoker("editor_bridge");
        var result = await invoker!.InvokeAsync(
            "open_file",
            parameters.RootElement,
            CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Value.ModuleId.Should().Be("sample_node_module");
        resolved.Value.Export.Operations.Should().Contain(operation => operation.Name == "open_file");
        ((IForeignModuleProtocolContractModule)foreignHost.Module).RequiredProtocolContracts.Should()
            .Contain(requirement => requirement.ContractName == "theme_bridge" && requirement.Optional);
        result.GetProperty("contractName").GetString().Should().Be("editor_bridge");
        result.GetProperty("operation").GetString().Should().Be("open_file");
        result.GetProperty("parameters").GetProperty("path").GetString().Should().Be("README.md");
    }

    [Test]
    public void ProtocolContractsParticipateInInitializationOrder()
    {
        var registry = new ModuleRegistry();
        var provider = new ProtocolProviderModule();
        var consumer = new ProtocolConsumerModule();

        registry.Register(consumer);
        registry.Register(provider);
        var order = registry.GetInitializationOrder(out var excluded);

        excluded.Should().BeEmpty();
        order.Should().ContainInOrder(provider.Id, consumer.Id);
    }

    [Test]
    public void MissingRequiredProtocolContractExcludesConsumerFromInitialization()
    {
        var registry = new ModuleRegistry();
        var consumer = new ProtocolConsumerModule();

        registry.Register(consumer);
        var order = registry.GetInitializationOrder(out var excluded);

        order.Should().BeEmpty();
        excluded.Should().ContainSingle()
            .Which.Should().Be((consumer.Id, "Unsatisfied contract(s): editor_bridge"));
        registry.GetUnsatisfiedProtocolRequirements(consumer.Id).Should()
            .ContainSingle(requirement => requirement.ContractName == "editor_bridge");
    }

    private static ModuleManifest Manifest() =>
        new(
            "sample_node_module",
            "Sample Node Module",
            "1.0.0",
            "snm",
            "dist/server.js",
            "0.0.0");

    private static ModuleManifestRuntimeInfo RuntimeInfo() =>
        new(ModuleManifestRuntimeInfo.Node, "dist/server.js");

    private static ForeignModuleHostLaunchOptions CreateLaunchOptions(TestWorkspace workspace)
    {
        var helperPath = ResolveSidecarHelperPath();
        return new ForeignModuleHostLaunchOptions
        {
            ExecutablePath = "dotnet",
            Arguments = [helperPath, "--mode", "normal"],
            WorkingDirectory = Path.GetDirectoryName(helperPath),
            ModuleDirectory = workspace.ModuleDir,
            ModuleDataDirectory = workspace.DataDir,
            ControlAddress = new Uri($"http://127.0.0.1:{GetFreeTcpPort()}"),
            ControlToken = "run-token",
            StartupTimeout = TimeSpan.FromSeconds(5),
            ShutdownTimeout = TimeSpan.FromSeconds(2),
            HostVersion = "0.1.0-beta",
            Environment = new Dictionary<string, string>
            {
                ["SHARPCLAW_TEST_TOOL_PREFIX"] = "snm",
            },
        };
    }

    private static ForeignModuleProtocolContractExport EditorBridgeExport() =>
        new(
            "editor_bridge",
            EmptyObjectSchema(),
            [
                new ForeignModuleProtocolContractOperation(
                    "open_file",
                    EmptyObjectSchema(),
                    EmptyObjectSchema())
            ]);

    private static JsonElement EmptyObjectSchema()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return document.RootElement.Clone();
    }

    private static string ResolveSidecarHelperPath()
    {
        var root = ResolveRepoRoot();
        var configuration = Directory.GetParent(TestContext.CurrentContext.TestDirectory)!.Name;
        var helperPath = Path.Combine(
            root,
            "SharpClaw.Tests.ForeignSidecar",
            "bin",
            configuration,
            "net10.0",
            "SharpClaw.Tests.ForeignSidecar.dll");

        File.Exists(helperPath).Should().BeTrue(
            $"foreign sidecar helper must be built before tests run: '{helperPath}'");
        return helperPath;
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

    private sealed class ProtocolProviderModule : ISharpClawModule, IForeignModuleProtocolContractExporter
    {
        private readonly ForeignModuleProtocolContractExport _export = EditorBridgeExport();

        public string Id => "protocol_provider";
        public string DisplayName => "Protocol Provider";
        public string ToolPrefix => "pp";
        public IReadOnlyList<ForeignModuleProtocolContractExport> ExportedProtocolContracts => [_export];
        public IReadOnlyList<ForeignModuleProtocolContractRequirement> RequiredProtocolContracts => [];

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

        public Task<string> ExecuteToolAsync(
            string toolName,
            JsonElement parameters,
            AgentJobContext job,
            IServiceProvider scopedServices,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public IForeignModuleProtocolContractInvoker GetProtocolContractInvoker(string contractName) =>
            new StaticProtocolContractInvoker(_export);
    }

    private sealed class ProtocolConsumerModule : ISharpClawModule, IForeignModuleProtocolContractModule
    {
        public string Id => "protocol_consumer";
        public string DisplayName => "Protocol Consumer";
        public string ToolPrefix => "pc";
        public IReadOnlyList<ForeignModuleProtocolContractExport> ExportedProtocolContracts => [];
        public IReadOnlyList<ForeignModuleProtocolContractRequirement> RequiredProtocolContracts =>
            [new("editor_bridge")];

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

        public Task<string> ExecuteToolAsync(
            string toolName,
            JsonElement parameters,
            AgentJobContext job,
            IServiceProvider scopedServices,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StaticProtocolContractInvoker(
        ForeignModuleProtocolContractExport export) : IForeignModuleProtocolContractInvoker
    {
        public string ContractName => export.ContractName;
        public IReadOnlyList<ForeignModuleProtocolContractOperation> Operations => export.Operations;

        public Task<JsonElement> InvokeAsync(
            string operation,
            JsonElement parameters,
            CancellationToken ct = default)
        {
            using var document = JsonDocument.Parse("""{"ok":true}""");
            return Task.FromResult(document.RootElement.Clone());
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root)
        {
            Root = root;
            ModuleDir = Path.Combine(root, "module");
            DataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(ModuleDir);
            Directory.CreateDirectory(DataDir);
        }

        public string Root { get; }
        public string ModuleDir { get; }
        public string DataDir { get; }

        public static TestWorkspace Create() =>
            new(Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "foreign-contracts",
                Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
