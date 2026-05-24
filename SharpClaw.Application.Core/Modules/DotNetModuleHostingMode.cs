using Microsoft.Extensions.Configuration;

namespace SharpClaw.Application.Core.Modules;

internal enum DotNetModuleHostingMode
{
    Manifest,
    SidecarOnly
}

internal static class DotNetModuleHostingModeOptions
{
    public const string ConfigKey = "Modules:DotNetHostingMode";

    public static DotNetModuleHostingMode Resolve(IConfiguration? configuration) =>
        Parse(configuration?[ConfigKey]);

    public static DotNetModuleHostingMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DotNetModuleHostingMode.Manifest;

        return value.Trim().ToLowerInvariant() switch
        {
            "manifest" or "default" or "auto" => DotNetModuleHostingMode.Manifest,
            "sidecar-only" or "sidecaronly" or "sidecar_only" => DotNetModuleHostingMode.SidecarOnly,
            _ => throw new InvalidOperationException(
                $"Unsupported {ConfigKey} value '{value}'. Allowed values are manifest and sidecar-only.")
        };
    }
}
