using System.Text.Json;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Describes a typed frontend extension point that a module contributes to
/// SharpClaw clients. Contributions are declarative: the module declares
/// where the UI belongs and which internal API endpoint backs it, while each
/// client chooses the native controls that fit that surface.
/// </summary>
public sealed record ModuleFrontendContribution(
    string Id,
    string ModuleId,
    FrontendContributionPoint Point,
    string BuilderKey,
    string Label,
    string? Icon = null,
    string? Tooltip = null,
    string? RequiredModuleId = null,
    int Order = 0,
    ModuleFrontendAction? Action = null,
    ModuleFrontendForm? Form = null,
    ModuleFrontendList? List = null,
    IReadOnlyDictionary<string, JsonElement>? Metadata = null);

public enum FrontendContributionPoint
{
    SettingsPage,
    ChatInputAction,
    ResourcePanel,
    DashboardCard,
    NavigationItem,
}

public sealed record ModuleFrontendAction(
    string Method,
    string InternalApiPath,
    string? RequestSchemaKey = null,
    string? ResponseMode = null);

public sealed record ModuleFrontendForm(
    string? ReadInternalApiPath = null,
    string? SaveInternalApiPath = null,
    IReadOnlyList<ModuleFrontendField>? Fields = null);

public sealed record ModuleFrontendField(
    string Key,
    string Label,
    string FieldType,
    bool Required = false,
    string? HelpText = null,
    string? Placeholder = null,
    string? DefaultValue = null);

public sealed record ModuleFrontendList(
    string ListInternalApiPath,
    string? SyncInternalApiPath = null,
    string? DeleteInternalApiPathTemplate = null,
    string? EmptyText = null,
    IReadOnlyList<ModuleFrontendListColumn>? Columns = null);

public sealed record ModuleFrontendListColumn(string Key, string Label);

public sealed record ModuleFrontendContributionResponse(
    IReadOnlyList<ModuleFrontendContribution> Items);
