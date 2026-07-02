using System.Text.Json;

namespace SharpClaw.Tests.Core;

[TestFixture]
public sealed class ModuleRuntimeProtocolCoreTests
{
    [Test]
    public void ForeignModuleProtocolSurface_ComesFromCoreAssembly()
    {
        typeof(ForeignModuleProtocol).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleHostCapabilityProtocol).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleEndpointResponseMode).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleCapability).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");

        ForeignModuleProtocol.Version.Should().Be(1);
        ForeignModuleProtocol.HandshakePath.Should().Be("/.sharpclaw/handshake");
        ForeignModuleHostCapabilityProtocol.TaskLaunchPath
            .Should().Be("/.sharpclaw/host/tasks/launch");
        ForeignModuleEndpointResponseMode.WebSocket.Should().Be("websocket");
        ForeignModuleCapability.ProviderPlugins.Should().Be("providerPlugins");
    }

    [Test]
    public void ForeignModuleSidecarProtocolModels_ComeFromCoreAndUsePackageContracts()
    {
        typeof(ForeignModuleHandshakeRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleDiscoveryResponse).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleToolDescriptor).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleProtocolContractInvocationRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleTaskTriggerAttributeHandleRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleProviderChatCompletionRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleProviderPluginDescriptor).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");

        typeof(SharpClaw.Contracts.Modules.ModuleManifest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Contracts");
        typeof(SharpClaw.Contracts.Tasks.TaskTriggerDefinition).Assembly.GetName().Name
            .Should().Be("SharpClaw.Contracts");
        typeof(SharpClaw.Contracts.Providers.ChatCompletionMessage).Assembly.GetName().Name
            .Should().Be("SharpClaw.Contracts");
        typeof(SharpClaw.Contracts.Providers.ProviderCostSeed).Assembly.GetName().Name
            .Should().Be("SharpClaw.Contracts");

        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var descriptor = new ForeignModuleToolDescriptor(
            "sample",
            "Sample tool",
            schema,
            Permission: new ForeignModulePermissionDescriptor(IsPerResource: false));

        descriptor.ToModuleToolDefinition().Name.Should().Be("sample");

        var health = new ForeignModuleHealthResponse(true, Details: new Dictionary<string, JsonElement>
        {
            ["queueDepth"] = JsonSerializer.SerializeToElement(3),
        });

        health.ToModuleHealthStatus().Details.Should().ContainKey("queueDepth");
    }

    [Test]
    public void ModuleManifestRuntimeInfo_ParsesAndNormalizesInCore()
    {
        var runtimeInfo = ModuleManifestRuntimeInfo.FromJson(
            """
            {
              "runtime": " DOTNET ",
              "entrypoint": "ignored",
              "moduleType": "SharpClaw.Tests.FakeModule",
              "hostMode": "inprocess"
            }
            """);

        typeof(ModuleManifestRuntimeInfo).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        runtimeInfo.Runtime.Should().Be(ModuleManifestRuntimeInfo.DotNet);
        runtimeInfo.ModuleType.Should().Be("SharpClaw.Tests.FakeModule");
        runtimeInfo.HostMode.Should().Be(ModuleManifestRuntimeInfo.HostModeInProcess);
        runtimeInfo.IsDotNet.Should().BeTrue();
        runtimeInfo.IsInProcessHostMode.Should().BeTrue();
    }

    [Test]
    public void ModuleManifestRuntimeInfo_RejectsPathLikeDotNetEntryAssembly()
    {
        var manifest = JsonSerializer.Deserialize<SharpClaw.Contracts.Modules.ModuleManifest>(
            """
            {
              "id": "bad_module",
              "displayName": "Bad",
              "toolPrefix": "bad",
              "entryAssembly": "nested/bad.dll"
            }
            """)!;

        var act = () => ModuleManifestRuntimeInfo.DotNetDefault
            .EnsureDotNetEntryAssembly(manifest);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*entryAssembly*file name*");
    }

    [Test]
    public void ForeignModuleHostCapabilityDtos_ComeFromCoreAndUseContractsModuleInfo()
    {
        typeof(ForeignModuleConfigGetRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleTaskLaunchRequest).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleTaskStepInvocationDescriptor).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleTaskStepExecutionResponse).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(ForeignModuleInfoListResponse).Assembly.GetName().Name
            .Should().Be("SharpClaw.Core");
        typeof(SharpClaw.Contracts.Modules.ModuleInfo).Assembly.GetName().Name
            .Should().Be("SharpClaw.Contracts");

        var descriptor = new ForeignModuleTaskStepInvocationDescriptor(
            "module.step",
            Arguments: ["one"],
            Body:
            [
                new ForeignModuleTaskStepInvocationDescriptor("module.child")
            ]);

        descriptor.StepKey.Should().Be("module.step");
        descriptor.Body.Should().ContainSingle()
            .Which.StepKey.Should().Be("module.child");
    }
}
