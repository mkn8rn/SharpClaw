using System.Text.Json;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// A lightweight inline tool that executes within the ChatService streaming loop.
/// Does not create a job record. Suitable for fast, stateless operations.
/// </summary>
public sealed record ModuleInlineToolDefinition(
    /// <summary>Tool name (e.g. "wait", "list_accessible_threads").</summary>
    string Name,

    /// <summary>Description shown to the LLM.</summary>
    string Description,

    /// <summary>JSON Schema for parameters.</summary>
    JsonElement ParametersSchema,

    /// <summary>
    /// Optional permission requirement. If <c>null</c>, the tool is always allowed
    /// (subject to <c>ToolAwarenessSet</c> filtering). If set, the host evaluates
    /// the permission before calling <see cref="ISharpClawModule.ExecuteInlineToolAsync"/>.
    /// </summary>
    ModuleToolPermission? Permission = null,

    /// <summary>
    /// Optional alias names that resolve to this tool in the registry.
    /// Same semantics as <see cref="ModuleToolDefinition.Aliases"/>.
    /// </summary>
    IReadOnlyList<string>? Aliases = null
);
