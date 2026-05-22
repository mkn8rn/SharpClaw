using System.Text.Json;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Application.Core.Modules.Foreign;

internal sealed record ForeignModuleHandshakeRequest(
    int ProtocolVersion,
    string ModuleId,
    string ToolPrefix,
    string? HostVersion = null);

internal sealed record ForeignModuleHandshakeResponse(
    int ProtocolVersion,
    string ModuleId,
    string ToolPrefix,
    string Runtime,
    string RuntimeVersion,
    IReadOnlyList<string>? Capabilities = null);

internal sealed record ForeignModuleLifecycleRequest(
    int ProtocolVersion,
    string ModuleId);

internal sealed record ForeignModuleLifecycleResponse(
    bool Accepted = true,
    string? Message = null);

internal sealed record ForeignModuleHealthResponse(
    bool IsHealthy,
    string? Message = null,
    IReadOnlyDictionary<string, JsonElement>? Details = null)
{
    public ModuleHealthStatus ToModuleHealthStatus() =>
        new(IsHealthy, Message, Details?.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
}

public sealed record ForeignModuleDiscoveryResponse(
    IReadOnlyList<ForeignModuleEndpointDescriptor>? Endpoints = null,
    IReadOnlyList<ForeignModuleToolDescriptor>? Tools = null,
    IReadOnlyList<ForeignModuleInlineToolDescriptor>? InlineTools = null,
    IReadOnlyList<ForeignModuleProtocolContractExportDescriptor>? ProtocolContracts = null,
    IReadOnlyList<ForeignModuleProtocolContractRequirementDescriptor>? RequiredProtocolContracts = null,
    IReadOnlyList<ForeignModuleHeaderTagDescriptor>? HeaderTags = null,
    IReadOnlyList<ForeignModuleResourceTypeDescriptor>? ResourceTypes = null,
    IReadOnlyList<ForeignModuleGlobalFlagDescriptor>? GlobalFlags = null,
    IReadOnlyList<ModuleUiContribution>? UiContributions = null,
    IReadOnlyList<ModuleFrontendContribution>? FrontendContributions = null,
    IReadOnlyList<ForeignModuleCliCommandDescriptor>? CliCommands = null);

public sealed record ForeignModuleEndpointDescriptor(
    string Method,
    string RoutePattern,
    string ResponseMode,
    string? AuthPolicy = null,
    ForeignModulePermissionDescriptor? Permission = null,
    string? ContributionId = null,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);

public sealed record ForeignModulePermissionDescriptor(
    bool IsPerResource,
    string? DelegateTo = null)
{
    public ModuleToolPermission ToModuleToolPermission() =>
        string.IsNullOrWhiteSpace(DelegateTo)
            ? new ModuleToolPermission(
                IsPerResource,
                (_, _, _, _) => Task.FromResult(
                    AgentActionResult.Approve(
                        "Foreign module tool does not require host permission.",
                        PermissionClearance.Unset)))
            : new ModuleToolPermission(IsPerResource, Check: null, DelegateTo);
}

public sealed record ForeignModuleToolDescriptor(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ForeignModulePermissionDescriptor? Permission = null,
    int? TimeoutSeconds = null,
    IReadOnlyList<string>? Aliases = null,
    bool SupportsStreaming = false,
    ModuleJobCompletionBehavior CompletionBehavior =
        ModuleJobCompletionBehavior.CompleteWhenExecutionReturns)
{
    public ModuleToolDefinition ToModuleToolDefinition() =>
        new(
            Name,
            Description,
            ParametersSchema,
            (Permission ?? new ForeignModulePermissionDescriptor(IsPerResource: false))
                .ToModuleToolPermission(),
            TimeoutSeconds,
            Aliases);
}

public sealed record ForeignModuleInlineToolDescriptor(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ForeignModulePermissionDescriptor? Permission = null,
    IReadOnlyList<string>? Aliases = null)
{
    public ModuleInlineToolDefinition ToModuleInlineToolDefinition() =>
        new(
            Name,
            Description,
            ParametersSchema,
            Permission?.ToModuleToolPermission(),
            Aliases);
}

public sealed record ForeignModuleHeaderTagDescriptor(
    string Name,
    bool SupportsContext = true)
{
    internal ModuleHeaderTag ToModuleHeaderTag(
        ModuleManifest manifest,
        ForeignModuleProtocolClient client)
    {
        var tag = new ModuleHeaderTag(
            Name,
            async (_, ct) => (await client.ResolveHeaderTagAsync(
                manifest,
                Name,
                context: null,
                ct)).Value);

        return SupportsContext
            ? tag with
            {
                ResolveWithContext = async (_, context, ct) =>
                    (await client.ResolveHeaderTagAsync(
                        manifest,
                        Name,
                        context,
                        ct)).Value
            }
            : tag;
    }
}

public sealed record ForeignModuleResourceTypeDescriptor(
    string ResourceType,
    string GrantLabel,
    string DelegateMethodName,
    string? DefaultResourceKey = null,
    bool SupportsLookupItems = false)
{
    internal ModuleResourceTypeDescriptor ToModuleResourceTypeDescriptor(
        ModuleManifest manifest,
        ForeignModuleProtocolClient client) =>
        new(
            ResourceType,
            GrantLabel,
            DelegateMethodName,
            async (_, ct) => await client.LoadResourceIdsAsync(
                manifest,
                ResourceType,
                ct),
            SupportsLookupItems
                ? async (_, ct) =>
                {
                    var items = await client.LoadResourceLookupItemsAsync(
                        manifest,
                        ResourceType,
                        ct);
                    return [.. items.Select(item => (item.Id, item.Name))];
                }
                : null,
            DefaultResourceKey);
}

public sealed record ForeignModuleGlobalFlagDescriptor(
    string FlagKey,
    string DisplayName,
    string Description,
    string DelegateMethodName)
{
    public ModuleGlobalFlagDescriptor ToModuleGlobalFlagDescriptor() =>
        new(FlagKey, DisplayName, Description, DelegateMethodName);
}

public sealed record ForeignModuleCliCommandDescriptor(
    string Name,
    string[]? Aliases,
    ModuleCliScope Scope,
    string Description,
    string[]? UsageLines)
{
    internal ModuleCliCommand ToModuleCliCommand(
        ModuleManifest manifest,
        ForeignModuleProtocolClient client) =>
        new(
            Name,
            Aliases ?? [],
            Scope,
            Description,
            UsageLines ?? [],
            async (args, _, ct) =>
            {
                var result = await client.ExecuteCliCommandAsync(
                    manifest,
                    Name,
                    args,
                    ct);

                if (!string.IsNullOrEmpty(result.Stdout))
                    Console.Out.Write(result.Stdout);
                if (!string.IsNullOrEmpty(result.Stderr))
                    Console.Error.Write(result.Stderr);
            });
}

internal sealed record ForeignModuleToolExecutionRequest(
    int ProtocolVersion,
    string ModuleId,
    string ToolName,
    JsonElement Parameters,
    ForeignModuleAgentJobContext Job);

internal sealed record ForeignModuleInlineToolExecutionRequest(
    int ProtocolVersion,
    string ModuleId,
    string ToolName,
    JsonElement Parameters,
    ForeignModuleInlineToolContext Context);

internal sealed record ForeignModuleToolExecutionResponse(
    string? Result = null,
    ModuleJobCompletionBehavior? CompletionBehavior = null);

internal sealed record ForeignModuleAgentJobContext(
    Guid JobId,
    Guid AgentId,
    Guid ChannelId,
    Guid? ResourceId,
    string? ActionKey)
{
    public static ForeignModuleAgentJobContext From(AgentJobContext job) =>
        new(job.JobId, job.AgentId, job.ChannelId, job.ResourceId, job.ActionKey);
}

internal sealed record ForeignModuleInlineToolContext(
    Guid AgentId,
    Guid ChannelId,
    Guid? ThreadId,
    string ToolCallId)
{
    public static ForeignModuleInlineToolContext From(InlineToolContext context) =>
        new(context.AgentId, context.ChannelId, context.ThreadId, context.ToolCallId);
}

internal sealed record ForeignModuleToolStreamEvent(
    string? Delta = null,
    string? Result = null,
    string? Error = null,
    bool IsFinal = false);

internal sealed record ForeignModuleHeaderTagResolveRequest(
    int ProtocolVersion,
    string ModuleId,
    string Name,
    ModuleHeaderTagContext? Context = null);

internal sealed record ForeignModuleHeaderTagResolveResponse(string Value);

internal sealed record ForeignModuleResourceRequest(
    int ProtocolVersion,
    string ModuleId,
    string ResourceType);

public sealed record ForeignModuleResourceLookupItem(Guid Id, string Name);

internal sealed record ForeignModuleResourceIdsResponse(IReadOnlyList<Guid> Ids);

internal sealed record ForeignModuleResourceLookupResponse(
    IReadOnlyList<ForeignModuleResourceLookupItem> Items);

internal sealed record ForeignModuleCliExecutionRequest(
    int ProtocolVersion,
    string ModuleId,
    string CommandName,
    IReadOnlyList<string> Args);

internal sealed record ForeignModuleCliExecutionResponse(
    bool Success,
    string? Stdout = null,
    string? Stderr = null);

public sealed record ForeignModuleProtocolContractExportDescriptor(
    string ContractName,
    JsonElement Schema,
    IReadOnlyList<ForeignModuleProtocolContractOperation> Operations,
    string? Description = null)
{
    public ForeignModuleProtocolContractExport ToProtocolContractExport() =>
        new(ContractName, Schema, Operations, Description);
}

public sealed record ForeignModuleProtocolContractRequirementDescriptor(
    string ContractName,
    JsonElement? Schema = null,
    bool Optional = false,
    string? Description = null)
{
    public ForeignModuleProtocolContractRequirement ToProtocolContractRequirement() =>
        new(ContractName, Schema, Optional, Description);
}

internal sealed record ForeignModuleProtocolContractInvocationRequest(
    int ProtocolVersion,
    string ModuleId,
    string ContractName,
    string Operation,
    JsonElement Parameters);

internal sealed record ForeignModuleProtocolContractInvocationResponse(
    JsonElement Result);
