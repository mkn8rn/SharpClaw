using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Application.Core.Modules.Foreign;

internal sealed class ForeignModuleProxy(
    ModuleManifest manifest,
    ForeignModuleProtocolClient client,
    Func<Task> shutdown)
    : ISharpClawModule, IForeignModuleProtocolContractExporter
{
    private IReadOnlyList<ForeignModuleToolDescriptor> _tools = [];
    private IReadOnlyList<ForeignModuleInlineToolDescriptor> _inlineTools = [];
    private IReadOnlyList<ForeignModuleProtocolContractExportDescriptor> _protocolContracts = [];
    private IReadOnlyList<ForeignModuleProtocolContractRequirementDescriptor> _requiredProtocolContracts = [];
    private IReadOnlyList<ForeignModuleHeaderTagDescriptor> _headerTags = [];
    private IReadOnlyList<ForeignModuleResourceTypeDescriptor> _resourceTypes = [];
    private IReadOnlyList<ForeignModuleGlobalFlagDescriptor> _globalFlags = [];
    private IReadOnlyList<ModuleUiContribution> _uiContributions = [];
    private IReadOnlyList<ModuleFrontendContribution> _frontendContributions = [];
    private IReadOnlyList<ForeignModuleCliCommandDescriptor> _cliCommands = [];

    public string Id => manifest.Id;
    public string DisplayName => manifest.DisplayName;
    public string ToolPrefix => manifest.ToolPrefix;

    public void ConfigureServices(IServiceCollection services)
    {
    }

    public IReadOnlyList<ModuleToolDefinition> GetToolDefinitions() =>
        [.. _tools.Select(tool => tool.ToModuleToolDefinition())];

    public IReadOnlyList<ModuleInlineToolDefinition> GetInlineToolDefinitions() =>
        [.. _inlineTools.Select(tool => tool.ToModuleInlineToolDefinition())];

    public IReadOnlyList<ModuleHeaderTag>? GetHeaderTags() =>
        [.. _headerTags.Select(tag => tag.ToModuleHeaderTag(manifest, client))];

    public IReadOnlyList<ModuleResourceTypeDescriptor> GetResourceTypeDescriptors() =>
        [.. _resourceTypes.Select(resource => resource.ToModuleResourceTypeDescriptor(manifest, client))];

    public IReadOnlyList<ModuleGlobalFlagDescriptor> GetGlobalFlagDescriptors() =>
        [.. _globalFlags.Select(flag => flag.ToModuleGlobalFlagDescriptor())];

    public IReadOnlyList<ModuleUiContribution> GetUiContributions() => _uiContributions;

    public IReadOnlyList<ModuleFrontendContribution> GetFrontendContributions() => _frontendContributions;

    public IReadOnlyList<ModuleCliCommand>? GetCliCommands() =>
        [.. _cliCommands.Select(command => command.ToModuleCliCommand(manifest, client))];

    public IReadOnlyList<ForeignModuleProtocolContractExport> ExportedProtocolContracts =>
        [.. _protocolContracts.Select(contract => contract.ToProtocolContractExport())];

    public IReadOnlyList<ForeignModuleProtocolContractRequirement> RequiredProtocolContracts =>
        [.. _requiredProtocolContracts.Select(contract => contract.ToProtocolContractRequirement())];

    public void ApplyDiscovery(ForeignModuleDiscoveryResponse discovery)
    {
        _tools = discovery.Tools ?? [];
        _inlineTools = discovery.InlineTools ?? [];
        _protocolContracts = discovery.ProtocolContracts ?? [];
        _requiredProtocolContracts = discovery.RequiredProtocolContracts ?? [];
        _headerTags = discovery.HeaderTags ?? [];
        _resourceTypes = discovery.ResourceTypes ?? [];
        _globalFlags = discovery.GlobalFlags ?? [];
        _uiContributions = discovery.UiContributions ?? [];
        _frontendContributions = discovery.FrontendContributions ?? [];
        _cliCommands = discovery.CliCommands ?? [];
    }

    public IForeignModuleProtocolContractInvoker GetProtocolContractInvoker(string contractName)
    {
        var export = _protocolContracts.FirstOrDefault(contract =>
            string.Equals(contract.ContractName, contractName, StringComparison.Ordinal));
        if (export is null)
            throw new InvalidOperationException(
                $"Foreign module '{Id}' does not export protocol contract '{contractName}'.");

        return new ForeignModuleProtocolContractInvoker(
            manifest,
            client,
            export.ToProtocolContractExport());
    }

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
        ExecuteToolCoreAsync(toolName, parameters, job, ct);

    public ModuleJobCompletionBehavior GetJobCompletionBehavior(
        string toolName,
        JsonElement parameters,
        AgentJobContext job) =>
        _tools.FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal))
            ?.CompletionBehavior
        ?? ModuleJobCompletionBehavior.CompleteWhenExecutionReturns;

    public Task<string> ExecuteInlineToolAsync(
        string toolName,
        JsonElement parameters,
        InlineToolContext context,
        IServiceProvider scopedServices,
        CancellationToken ct) =>
        ExecuteInlineToolCoreAsync(toolName, parameters, context, ct);

    public IAsyncEnumerable<string>? ExecuteToolStreamingAsync(
        string toolName,
        JsonElement parameters,
        AgentJobContext job,
        IServiceProvider scopedServices,
        CancellationToken ct)
    {
        var tool = _tools.FirstOrDefault(tool =>
            string.Equals(tool.Name, toolName, StringComparison.Ordinal));
        return tool?.SupportsStreaming == true
            ? client.ExecuteToolStreamingAsync(manifest, toolName, parameters, job, ct)
            : null;
    }

    private async Task<string> ExecuteToolCoreAsync(
        string toolName,
        JsonElement parameters,
        AgentJobContext job,
        CancellationToken ct)
    {
        var response = await client.ExecuteToolAsync(manifest, toolName, parameters, job, ct);
        return response.Result ?? string.Empty;
    }

    private async Task<string> ExecuteInlineToolCoreAsync(
        string toolName,
        JsonElement parameters,
        InlineToolContext context,
        CancellationToken ct)
    {
        var response = await client.ExecuteInlineToolAsync(manifest, toolName, parameters, context, ct);
        return response.Result ?? string.Empty;
    }

    private sealed class ForeignModuleProtocolContractInvoker(
        ModuleManifest manifest,
        ForeignModuleProtocolClient client,
        ForeignModuleProtocolContractExport export) : IForeignModuleProtocolContractInvoker
    {
        public string ContractName => export.ContractName;
        public IReadOnlyList<ForeignModuleProtocolContractOperation> Operations => export.Operations;

        public async Task<JsonElement> InvokeAsync(
            string operation,
            JsonElement parameters,
            CancellationToken ct = default)
        {
            if (!Operations.Any(candidate => string.Equals(candidate.Name, operation, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Protocol contract '{ContractName}' does not define operation '{operation}'.");
            }

            var response = await client.InvokeProtocolContractAsync(
                manifest,
                ContractName,
                operation,
                parameters,
                ct);
            return response.Result;
        }
    }
}
