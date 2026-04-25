using System.Text.Json;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// A single tool exposed by a module.
/// </summary>
public sealed record ModuleToolDefinition(
    /// <summary>Tool name without prefix (e.g. "enumerate_windows").</summary>
    string Name,

    /// <summary>Description shown to the LLM.</summary>
    string Description,

    /// <summary>JSON Schema for parameters (same format as ChatToolDefinition).</summary>
    JsonElement ParametersSchema,

    /// <summary>Permission requirements for this tool.</summary>
    ModuleToolPermission Permission,

    /// <summary>
    /// Optional per-tool execution timeout in seconds.
    /// Overrides the manifest-level <c>executionTimeoutSeconds</c> for this tool.
    /// Falls back to manifest timeout, then to 30s if unset.
    /// </summary>
    int? TimeoutSeconds = null,

    /// <summary>
    /// Optional alias names that resolve to this tool in the registry.
    /// When set, <see cref="ModuleRegistry"/> emits these names instead of
    /// the prefixed canonical name in <c>GetAllToolDefinitions()</c>.
    /// Used for backwards compatibility when migrating tools to a new canonical name.
    /// </summary>
    IReadOnlyList<string>? Aliases = null
);
