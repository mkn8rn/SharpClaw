using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SharpClaw.Application.Core.Modules;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Modules.TestHarness;

namespace SharpClaw.Tests.TestHarness;

[TestFixture]
public sealed class TestHarnessArchitectureTests
{
    [Test]
    public void ProductionTemplateDisablesHarnessAndDevTemplateEnablesIt()
    {
        var root = FindSolutionRoot();
        var prod = LoadTemplate(Path.Combine(
            root,
            "SharpClaw.Application.Infrastructure",
            "Environment",
            ".env.template"));
        var dev = LoadTemplate(Path.Combine(
            root,
            "SharpClaw.Application.Infrastructure",
            "Environment",
            ".dev.env.template"));

        ModuleLoader.IsEnabledInConfig(TestHarnessConstants.ModuleId, prod).Should().BeFalse();
        ModuleLoader.IsEnabledInConfig(TestHarnessConstants.ModuleId, dev).Should().BeTrue();
    }

    [Test]
    public void DisabledModulesDoNotRemoveCoreChannelProperties()
    {
        var registry = new ModuleRegistry();

        registry.GetModule(TestHarnessConstants.ModuleId).Should().BeNull();
        typeof(ChannelDB).GetProperty(nameof(ChannelDB.AgentId)).Should().NotBeNull();
        typeof(ChannelDB).GetProperty(nameof(ChannelDB.AllowedAgents)).Should().NotBeNull();
        typeof(ChannelDB).GetProperty(nameof(ChannelDB.DisableChatHeader)).Should().NotBeNull();
        typeof(ChannelDB).GetProperty(nameof(ChannelDB.CustomChatHeader)).Should().NotBeNull();
    }

    [Test]
    public void RemovedBridgeAndUnsupportedCoreTestPathsStayOutOfCoreAndTests()
    {
        var root = FindSolutionRoot();
        var searchedRoots = new[]
        {
            Path.Combine(root, "SharpClaw.Application.Core"),
            Path.Combine(root, "SharpClaw.Tests")
        };
        var banned = new[]
        {
            "ChatProcessing" + "Bridge",
            "WellKnown" + "ProviderKeys",
            "trans" + "cription",
            "voi" + "ce",
            "Whis" + "per"
        };

        var offenders = searchedRoots
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return banned
                    .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Select(term => $"{Path.GetRelativePath(root, path)} contains {term}");
            })
            .ToList();

        offenders.Should().BeEmpty();
    }

    private static IConfigurationRoot LoadTemplate(string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Modules:{TestHarnessConstants.ModuleId}"] = ReadModuleFlag(path)
            })
            .Build();

    private static string ReadModuleFlag(string path)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

        return doc.RootElement
            .GetProperty("Modules")
            .GetProperty(TestHarnessConstants.ModuleId)
            .GetString() ?? "false";
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpClaw.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SharpClaw.slnx from test assembly.");
    }
}
