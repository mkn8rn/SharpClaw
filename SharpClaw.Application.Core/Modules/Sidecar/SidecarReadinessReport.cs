using SharpClaw.Contracts.Modules;

namespace SharpClaw.Application.Core.Modules.Sidecar;

public sealed record ModuleSidecarReadinessReport(
    string ModuleId,
    string DisplayName,
    string ToolPrefix,
    string ModuleType,
    string AssemblyName,
    ModuleContributionInventory Contributions,
    ModuleServiceInventory Services,
    IReadOnlyList<SidecarReadinessFinding> Findings)
{
    public bool IsReadyForSidecarDefault =>
        Findings.All(finding => finding.Kind == SidecarReadinessFindingKind.CoveredByCurrentProtocol);

    public IReadOnlyList<SidecarReadinessFinding> Blockers =>
        [.. Findings.Where(finding => finding.Kind != SidecarReadinessFindingKind.CoveredByCurrentProtocol)];
}

public sealed record ModuleContributionInventory(
    int ToolCount,
    int InlineToolCount,
    int ResourceTypeDescriptorCount,
    int GlobalFlagDescriptorCount,
    int HeaderTagCount,
    int UiContributionCount,
    int FrontendContributionCount,
    int CliCommandCount,
    int ExportedClrContractCount,
    int RequiredClrContractCount,
    int ExportedProtocolContractCount,
    int RequiredProtocolContractCount,
    bool MapsEndpoints,
    bool OverridesInitialize,
    bool OverridesShutdown,
    bool OverridesSeedData,
    bool OverridesHealthCheck,
    bool OverridesStreamingTools,
    bool OverridesJobCompletionBehavior,
    bool IsTaskParserAware);

public sealed record ModuleServiceInventory(
    IReadOnlyList<ModuleServiceRegistration> Registrations,
    IReadOnlyList<string> ModuleDbContextTypes,
    IReadOnlyList<string> ProviderPluginRegistrations,
    IReadOnlyList<string> TaskRuntimeServiceRegistrations,
    IReadOnlyList<string> EventSinkRegistrations,
    IReadOnlyList<string> FactoryBackedServiceRegistrations,
    string? ConfigureServicesError = null);

public sealed record ModuleServiceRegistration(
    string ServiceType,
    string? ImplementationType,
    string Lifetime,
    bool UsesFactory,
    bool UsesInstance);

public sealed record SidecarReadinessFinding(
    SidecarReadinessFindingKind Kind,
    string Key,
    string Detail);

public enum SidecarReadinessFindingKind
{
    CoveredByCurrentProtocol,
    RequiresProtocolSurface,
    RequiresHostCapability,
    RequiresStoragePlan,
    RequiresClrContractBridge,
    RequiresManualReview
}
