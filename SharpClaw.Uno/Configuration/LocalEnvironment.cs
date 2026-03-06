using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace SharpClaw.Configuration;

/// <summary>
/// Loads environment configuration from <c>Environment/.env</c> (always)
/// and <c>Environment/.dev.env</c> (development only) relative to the assembly location.
/// Creates a default <c>.env</c> if one does not exist.
/// </summary>
public static class LocalEnvironment
{
    public const string DefaultApiUrl = "http://127.0.0.1:48923";

    private const string DefaultEnvContent =
        """
        {
          // SharpClaw Client Environment Configuration
          // Values here are loaded for all environments.

          // -- API Server -----------------------------------------------
          // Base URL for the SharpClaw API service.
          "Api": { "Url": "http://127.0.0.1:48923" }
        }
        """;

    /// <summary>
    /// Reads <c>Api:Url</c> from the environment files, falling back to
    /// <see cref="DefaultApiUrl"/> when not configured.
    /// </summary>
    public static string LoadApiUrl(bool isDevelopment = false)
    {
        var config = BuildConfiguration(isDevelopment);
        return config["Api:Url"] ?? DefaultApiUrl;
    }

    public static IConfigurationBuilder AddLocalEnvironment(
        this IConfigurationBuilder builder, bool isDevelopment = false)
    {
        var envDir = GetEnvironmentDirectory();
        EnsureEnvironmentFile(envDir);

        if (!Directory.Exists(envDir))
            return builder;

        var fileProvider = new PhysicalFileProvider(envDir, ExclusionFilters.None);

        builder.AddJsonFile(fileProvider, ".env", optional: true, reloadOnChange: false);

        if (isDevelopment)
            builder.AddJsonFile(fileProvider, ".dev.env", optional: true, reloadOnChange: false);

        return builder;
    }

    private static IConfiguration BuildConfiguration(bool isDevelopment)
    {
        var builder = new ConfigurationBuilder();
        builder.AddLocalEnvironment(isDevelopment);
        return builder.Build();
    }

    private static string GetEnvironmentDirectory() =>
        Path.Combine(
            Path.GetDirectoryName(typeof(LocalEnvironment).Assembly.Location)!,
            "Environment");

    private static void EnsureEnvironmentFile(string envDir)
    {
        var envFile = Path.Combine(envDir, ".env");
        if (File.Exists(envFile))
            return;

        try
        {
            Directory.CreateDirectory(envDir);
            File.WriteAllText(envFile, DefaultEnvContent);
        }
        catch
        {
            // Best-effort — read-only or restricted file system.
        }
    }
}
