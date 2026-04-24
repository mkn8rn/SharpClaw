namespace SharpClaw.Application.Infrastructure.Tasks.Models;

/// <summary>
/// Discriminates the kind of environment requirement declared on a task class.
/// </summary>
public enum TaskRequirementKind
{
    /// <summary>A provider of the given type must exist and have an API key configured.</summary>
    RequiresProvider,

    /// <summary>At least one model with the given capability flag must exist in the catalogue.</summary>
    RequiresModelCapability,

    /// <summary>A model with the given name or custom ID must exist in the catalogue.</summary>
    RequiresModel,

    /// <summary>The named module must be loaded and enabled (hard error).</summary>
    RequiresModule,

    /// <summary>The named module should be present but is not mandatory (warning).</summary>
    RecommendsModule,

    /// <summary>The task must run on the declared OS platform (static check).</summary>
    RequiresPlatform,

    /// <summary>The caller must hold the given permission flag key.</summary>
    RequiresPermission,

    /// <summary>The annotated string/Guid parameter must resolve to a real model at instance creation.</summary>
    ModelIdParameter,

    /// <summary>The model resolved from the annotated parameter must have the given capability.</summary>
    RequiresCapabilityParameter,
}
