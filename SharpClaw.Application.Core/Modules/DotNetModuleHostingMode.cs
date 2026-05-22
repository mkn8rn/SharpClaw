using Microsoft.Extensions.Configuration;

namespace SharpClaw.Application.Core.Modules;

internal enum DotNetModuleHostingMode
{
    Manifest,
    InProcess,
    SidecarOnly
}

internal static class DotNetModuleHostingModeOptions
{
    public const string ConfigKey = "Modules:DotNetHostingMode";
    public const string ForceInProcessKey = "Modules:ForceInProcessDotNetSidecars";

    public static DotNetModuleHostingMode Resolve(IConfiguration? configuration)
    {
        if (configuration?.GetValue(ForceInProcessKey, false) == true)
            return DotNetModuleHostingMode.InProcess;

        return Parse(configuration?[ConfigKey]);
    }

    public static DotNetModuleHostingMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DotNetModuleHostingMode.Manifest;

        return value.Trim().ToLowerInvariant() switch
        {
            "manifest" or "default" or "auto" => DotNetModuleHostingMode.Manifest,
            "in-process" or "inprocess" or "in_process" => DotNetModuleHostingMode.InProcess,
            "sidecar-only" or "sidecaronly" or "sidecar_only" => DotNetModuleHostingMode.SidecarOnly,
            _ => throw new InvalidOperationException(
                $"Unsupported {ConfigKey} value '{value}'. Allowed values are manifest, in-process, and sidecar-only.")
        };
    }
}
