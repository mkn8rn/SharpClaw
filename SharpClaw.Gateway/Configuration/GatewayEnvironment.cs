using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace SharpClaw.Gateway.Configuration;

/// <summary>
/// Loads gateway environment configuration from <c>Environment/.env</c>
/// (always) and <c>Environment/.dev.env</c> (development only) relative
/// to the assembly location.  Creates a default <c>.env</c> if one does
/// not exist.
/// <para>
/// This follows the same pattern used by the Core and Interface
/// environments — JSON-with-comments loaded via
/// <see cref="PhysicalFileProvider"/>.
/// </para>
/// </summary>
public static class GatewayEnvironment
{
    private const string DefaultEnvContent =
        """
        {
          // SharpClaw Gateway Environment Configuration
          // Values here are loaded for all environments.

          // ── Internal API ───────────────────────────────────────────
          // Base URL of the internal SharpClaw Application API.
          "InternalApi": { "BaseUrl": "http://127.0.0.1:48923" },

          // ── Endpoint Toggles ───────────────────────────────────────
          // Master kill-switch and per-group enable/disable.
          // Set "Enabled" to false to disable the entire gateway.
          "Gateway": {
            "Endpoints": {
              "Enabled": "true",
              "Auth": "true",
              "Agents": "true",
              "Channels": "true",
              "ChannelContexts": "true",
              "Chat": "true",
              "ChatStream": "true",
              "Threads": "true",
              "ThreadChat": "true",
              "Jobs": "true",
              "Models": "true",
              "Providers": "true",
              "Roles": "true",
              "Users": "true",
              "AudioDevices": "true",
              "Transcription": "true",
              "TranscriptionStreaming": "true",
              "Cost": "true",
              "Bots": "true"
            },

            // ── Bot Integrations ───────────────────────────────────────
            "Bots": {
              "Telegram": {
                "Enabled": "false",
                "BotToken": ""
              },
              "Discord": {
                "Enabled": "false",
                "BotToken": ""
              }
            }
          }
        }
        """;

    public static IConfigurationBuilder AddGatewayEnvironment(
        this IConfigurationBuilder builder, bool isDevelopment = false)
    {
        var envDir = Path.Combine(
            Path.GetDirectoryName(typeof(GatewayEnvironment).Assembly.Location)!,
            "Environment");

        EnsureEnvironmentFile(envDir);

        if (!Directory.Exists(envDir))
            return builder;

        // PhysicalFileProvider defaults to ExclusionFilters.Sensitive which
        // excludes dot-prefixed files (.env, .dev.env). Use None so the
        // configuration system can see them.
        var fileProvider = new PhysicalFileProvider(envDir, ExclusionFilters.None);

        builder.AddJsonFile(fileProvider, ".env", optional: true, reloadOnChange: false);

        if (isDevelopment)
            builder.AddJsonFile(fileProvider, ".dev.env", optional: true, reloadOnChange: false);

        return builder;
    }

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
