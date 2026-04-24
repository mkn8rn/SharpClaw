namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Provides read-only information about loaded modules.
/// Implemented host-side by <c>ModuleRegistry</c>; injected into modules
/// that need to introspect the module roster without referencing Core.
/// </summary>
public interface IModuleInfoProvider
{
    /// <summary>Returns lightweight descriptors for every currently registered module.</summary>
    IReadOnlyList<ModuleInfo> GetAllModules();
}

/// <summary>
/// Lightweight descriptor of a registered module, safe to cross the
/// module boundary without exposing Core's <c>ISharpClawModule</c>
/// implementation details.
/// </summary>
/// <param name="Id">Module identifier (e.g. <c>"sharpclaw_dangerous_shell"</c>).</param>
/// <param name="ToolPrefix">Short prefix used in tool and CLI names.</param>
/// <param name="ExportedContractNames">Names of contracts this module exports.</param>
public sealed record ModuleInfo(
    string Id,
    string ToolPrefix,
    IReadOnlyList<string> ExportedContractNames);
