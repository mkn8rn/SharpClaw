using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SharpClaw.Utils.Instances;
using SharpClaw.Utils.Security;

namespace SharpClaw.Infrastructure.Configuration;

/// <summary>
/// Loads environment configuration from an instance-scoped active file created
/// from the assembly-local Environment/.env template. Startup may encrypt the
/// active file after loading it, but it never mutates the published template.
/// </summary>
public static class LocalEnvironment
{
    private const string DefaultEnvContent =
        """
        {
          // SharpClaw Environment Configuration
          // Values here are loaded for all environments.

          "Jwt": {
            "Issuer": "SharpClaw",
            "Audience": "SharpClaw",
            "AccessTokenLifetime": "00:30:00",
            "RefreshTokenLifetime": "30.00:00:00"
          },

          "Auth": {
            "DisableApiKeyCheck": "false",
            "DisableAccessTokenCheck": "false"
          },

          "Admin": {
            "Username": "admin",
            "Password": "123456",
            "ReconcilePermissions": "true"
          },

          "EnvEditor": { "AllowNonAdmin": "false" }
        }
        """;

    public static IConfigurationBuilder AddLocalEnvironment(
        this IConfigurationBuilder builder,
        bool isDevelopment = false,
        SharpClawInstancePaths? instancePaths = null)
    {
        var templateDir = Path.Combine(
            Path.GetDirectoryName(typeof(LocalEnvironment).Assembly.Location)!,
            "Environment");
        var activeConfigDir = ResolveActiveConfigDirectory(instancePaths);

        return builder.AddLocalEnvironmentFrom(
            templateDir,
            activeConfigDir,
            isDevelopment,
            instancePaths);
    }

    internal static IConfigurationBuilder AddLocalEnvironmentFrom(
        this IConfigurationBuilder builder,
        string templateDir,
        string activeConfigDir,
        bool isDevelopment,
        SharpClawInstancePaths? instancePaths = null)
    {
        EnsureEnvironmentTemplate(templateDir, ".env", createDefaultWhenMissing: true);
        EnsureActiveEnvironmentFile(templateDir, activeConfigDir, ".env");
        AddEnvFile(builder, templateDir, activeConfigDir, ".env", instancePaths);

        if (isDevelopment && File.Exists(Path.Combine(templateDir, ".dev.env")))
        {
            EnsureEnvironmentTemplate(templateDir, ".dev.env", createDefaultWhenMissing: false);
            EnsureActiveEnvironmentFile(templateDir, activeConfigDir, ".dev.env");
            AddEnvFile(builder, templateDir, activeConfigDir, ".dev.env", instancePaths);
        }

        return builder;
    }

    public static string ResolveActiveEnvFilePath(SharpClawInstancePaths? instancePaths = null) =>
        Path.Combine(ResolveActiveConfigDirectory(instancePaths), ".env");

    private static void AddEnvFile(
        IConfigurationBuilder builder,
        string templateDir,
        string activeConfigDir,
        string fileName,
        SharpClawInstancePaths? instancePaths)
    {
        var activePath = Path.Combine(activeConfigDir, fileName);
        if (!File.Exists(activePath))
            return;

        if (!EncryptedEnvFile.IsEncryptedOnDisk(activePath))
        {
            AddPlaintextEnvFile(builder, activePath, instancePaths);
            return;
        }

        var key = EncryptionKeyResolver.ResolveKey(instancePaths);
        try
        {
            var json = EncryptedEnvFile.ReadAsync(activePath, key, CancellationToken.None)
                .GetAwaiter().GetResult();
            AddJson(builder, json);
        }
        catch (Exception ex) when (IsUnreadableEncryptedFile(ex))
        {
            QuarantineUnreadableActiveFile(activePath);
            CopyTemplateToActive(templateDir, activeConfigDir, fileName);
            AddPlaintextEnvFile(builder, activePath, instancePaths);
        }
    }

    private static void AddPlaintextEnvFile(
        IConfigurationBuilder builder,
        string activePath,
        SharpClawInstancePaths? instancePaths)
    {
        var json = File.ReadAllText(activePath);
        AddJson(builder, json);

        var key = EncryptionKeyResolver.ResolveKey(instancePaths);
        if (key is not null)
        {
            EncryptedEnvFile.WriteAsync(activePath, json, key, encrypt: true, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }

    private static void AddJson(IConfigurationBuilder builder, string json)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        builder.AddJsonStream(stream);
    }

    private static void EnsureEnvironmentTemplate(
        string templateDir,
        string fileName,
        bool createDefaultWhenMissing)
    {
        var templatePath = Path.Combine(templateDir, fileName);
        if (File.Exists(templatePath) && new FileInfo(templatePath).Length > 0)
        {
            if (EncryptedEnvFile.IsEncryptedOnDisk(templatePath))
            {
                throw new InvalidOperationException(
                    $"Environment template '{templatePath}' is encrypted. Published templates must be plaintext and portable.");
            }

            return;
        }

        if (!createDefaultWhenMissing)
            return;

        try
        {
            Directory.CreateDirectory(templateDir);
            File.WriteAllText(templatePath, DefaultEnvContent);
        }
        catch
        {
            // Best-effort: read-only deployments can still rely on existing templates.
        }
    }

    private static void EnsureActiveEnvironmentFile(
        string templateDir,
        string activeConfigDir,
        string fileName)
    {
        var activePath = Path.Combine(activeConfigDir, fileName);
        if (File.Exists(activePath) && new FileInfo(activePath).Length > 0)
            return;

        CopyTemplateToActive(templateDir, activeConfigDir, fileName);
    }

    private static void CopyTemplateToActive(
        string templateDir,
        string activeConfigDir,
        string fileName)
    {
        var templatePath = Path.Combine(templateDir, fileName);
        if (!File.Exists(templatePath) || new FileInfo(templatePath).Length == 0)
            return;

        if (EncryptedEnvFile.IsEncryptedOnDisk(templatePath))
        {
            throw new InvalidOperationException(
                $"Environment template '{templatePath}' is encrypted. Published templates must be plaintext and portable.");
        }

        Directory.CreateDirectory(activeConfigDir);
        File.Copy(templatePath, Path.Combine(activeConfigDir, fileName), overwrite: true);
    }

    private static void QuarantineUnreadableActiveFile(string activePath)
    {
        var quarantinePath = activePath + $".unreadable-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(activePath, quarantinePath, overwrite: true);
    }

    private static bool IsUnreadableEncryptedFile(Exception ex) =>
        ex is CryptographicException
        || ex is ArgumentException
        || ex.InnerException is not null && IsUnreadableEncryptedFile(ex.InnerException);

    private static string ResolveActiveConfigDirectory(SharpClawInstancePaths? instancePaths)
    {
        instancePaths ??= TryResolveBackendInstancePathsFromEnvironment();
        return instancePaths?.ConfigDirectory
            ?? Path.Combine(
                Path.GetDirectoryName(typeof(LocalEnvironment).Assembly.Location)!,
                "config");
    }

    private static SharpClawInstancePaths? TryResolveBackendInstancePathsFromEnvironment()
    {
        var instanceRoot = Environment.GetEnvironmentVariable("SHARPCLAW_INSTANCE_ROOT");
        var dataDir = Environment.GetEnvironmentVariable("SHARPCLAW_DATA_DIR");
        if (string.IsNullOrWhiteSpace(instanceRoot) && !string.IsNullOrWhiteSpace(dataDir))
            instanceRoot = Path.GetDirectoryName(Path.GetFullPath(dataDir));

        if (string.IsNullOrWhiteSpace(instanceRoot))
            return null;

        return new SharpClawInstancePaths(SharpClawInstanceKind.Backend, instanceRoot);
    }
}
