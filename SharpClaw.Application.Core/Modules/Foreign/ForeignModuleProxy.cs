using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Application.Core.Modules.Foreign;

internal sealed class ForeignModuleProxy(
    ModuleManifest manifest,
    ForeignModuleProtocolClient client,
    Func<Task> shutdown)
    : ISharpClawModule
{
    public string Id => manifest.Id;
    public string DisplayName => manifest.DisplayName;
    public string ToolPrefix => manifest.ToolPrefix;

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() => [];

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct) =>
        client.InitializeAsync(manifest, ct);

    public Task ShutdownAsync() => shutdown();

    public async Task<ModuleHealthStatus> HealthCheckAsync(CancellationToken ct) =>
        (await client.HealthAsync(ct)).ToModuleHealthStatus();

    public Task<string> ExecuteToolAsync(
        string toolName,
        JsonElement parameters,
        AgentJobContext job,
        IServiceProvider scopedServices,
        CancellationToken ct) =>
        throw new NotSupportedException(
            $"Foreign module '{Id}' does not support tool execution yet.");
}
