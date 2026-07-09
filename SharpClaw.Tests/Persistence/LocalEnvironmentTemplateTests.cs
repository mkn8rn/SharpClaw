using Microsoft.Extensions.Configuration;
using SharpClaw.Infrastructure.Configuration;
using SharpClaw.Utils.Instances;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public sealed class LocalEnvironmentTemplateTests
{
    [Test]
    public void FirstStartup_CopiesTemplateToInstanceConfigAndLeavesTemplatePlaintext()
    {
        using var workspace = TempWorkspace.Create();
        var templateJson = TemplateJson("TemplateAdmin");
        workspace.WriteTemplate(".env", templateJson);

        var configuration = new ConfigurationBuilder()
            .AddLocalEnvironmentFrom(
                workspace.TemplateDirectory,
                workspace.Paths.ConfigDirectory,
                isDevelopment: false,
                workspace.Paths)
            .Build();

        configuration["Admin:Username"].Should().Be("TemplateAdmin");
        File.ReadAllText(workspace.TemplatePath(".env")).Should().Be(templateJson);
        EncryptedEnvFile.IsEncryptedOnDisk(workspace.TemplatePath(".env")).Should().BeFalse();
        File.Exists(workspace.ActivePath(".env")).Should().BeTrue();
    }

    [Test]
    public async Task PlaintextActiveEnvironment_IsEncryptedAfterSuccessfulLoad()
    {
        using var workspace = TempWorkspace.Create();
        workspace.WriteTemplate(".env", TemplateJson("TemplateAdmin"));
        Directory.CreateDirectory(workspace.Paths.ConfigDirectory);
        await File.WriteAllTextAsync(workspace.ActivePath(".env"), TemplateJson("ActiveAdmin"));

        var configuration = new ConfigurationBuilder()
            .AddLocalEnvironmentFrom(
                workspace.TemplateDirectory,
                workspace.Paths.ConfigDirectory,
                isDevelopment: false,
                workspace.Paths)
            .Build();

        configuration["Admin:Username"].Should().Be("ActiveAdmin");
        EncryptedEnvFile.IsEncryptedOnDisk(workspace.ActivePath(".env")).Should().BeTrue();

        var key = Convert.FromBase64String(
            File.ReadAllText(workspace.Paths.GetSecretFilePath("encryption-key")).Trim());
        var decrypted = await EncryptedEnvFile.ReadAsync(workspace.ActivePath(".env"), key);
        decrypted.Should().Contain("ActiveAdmin");
    }

    [Test]
    public async Task WrongKeyEncryptedActiveEnvironment_IsQuarantinedAndRecreatedFromTemplate()
    {
        using var workspace = TempWorkspace.Create();
        workspace.WriteTemplate(".env", TemplateJson("RecoveredAdmin"));
        Directory.CreateDirectory(workspace.Paths.ConfigDirectory);
        await EncryptedEnvFile.WriteAsync(
            workspace.ActivePath(".env"),
            TemplateJson("ForeignSecretAdmin"),
            ApiKeyEncryptor.GenerateKey(),
            encrypt: true);

        var configuration = new ConfigurationBuilder()
            .AddLocalEnvironmentFrom(
                workspace.TemplateDirectory,
                workspace.Paths.ConfigDirectory,
                isDevelopment: false,
                workspace.Paths)
            .Build();

        configuration["Admin:Username"].Should().Be("RecoveredAdmin");
        Directory.GetFiles(workspace.Paths.ConfigDirectory, ".env.unreadable-*")
            .Should()
            .ContainSingle();
        EncryptedEnvFile.IsEncryptedOnDisk(workspace.ActivePath(".env")).Should().BeTrue();

        var key = Convert.FromBase64String(
            File.ReadAllText(workspace.Paths.GetSecretFilePath("encryption-key")).Trim());
        var recreated = await EncryptedEnvFile.ReadAsync(workspace.ActivePath(".env"), key);
        recreated.Should().Contain("RecoveredAdmin");
        recreated.Should().NotContain("ForeignSecretAdmin");
    }

    [Test]
    public async Task EncryptedTemplate_IsRejectedAsInvalidPortableTemplate()
    {
        using var workspace = TempWorkspace.Create();
        Directory.CreateDirectory(workspace.TemplateDirectory);
        await EncryptedEnvFile.WriteAsync(
            workspace.TemplatePath(".env"),
            TemplateJson("EncryptedTemplate"),
            ApiKeyEncryptor.GenerateKey(),
            encrypt: true);

        var act = () => new ConfigurationBuilder()
            .AddLocalEnvironmentFrom(
                workspace.TemplateDirectory,
                workspace.Paths.ConfigDirectory,
                isDevelopment: false,
                workspace.Paths)
            .Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*template*encrypted*plaintext*portable*");
    }

    private static string TemplateJson(string adminUser) =>
        $$"""
        {
          "Admin": {
            "Username": "{{adminUser}}",
            "Password": "123456"
          },
          "Auth": {
            "DisableApiKeyCheck": "false",
            "DisableAccessTokenCheck": "false"
          }
        }
        """;

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string root)
        {
            Root = root;
            TemplateDirectory = Path.Combine(root, "template", "Environment");
            Paths = new SharpClawInstancePaths(
                SharpClawInstanceKind.Backend,
                Path.Combine(root, "instance"));
        }

        public string Root { get; }
        public string TemplateDirectory { get; }
        public SharpClawInstancePaths Paths { get; }

        public static TempWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "SharpClaw.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempWorkspace(root);
        }

        public string TemplatePath(string fileName) => Path.Combine(TemplateDirectory, fileName);

        public string ActivePath(string fileName) => Path.Combine(Paths.ConfigDirectory, fileName);

        public void WriteTemplate(string fileName, string content)
        {
            Directory.CreateDirectory(TemplateDirectory);
            File.WriteAllText(TemplatePath(fileName), content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
