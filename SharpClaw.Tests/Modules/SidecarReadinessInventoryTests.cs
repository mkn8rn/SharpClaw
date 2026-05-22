using System.Reflection;
using SharpClaw.Application.Core.Modules.Sidecar;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class SidecarReadinessInventoryTests
{
    private static readonly string[] ExpectedModuleDlls =
    [
        "SharpClaw.Modules.AgentOrchestration.dll",
        "SharpClaw.Modules.EditorCommon.dll",
        "SharpClaw.Modules.Metrics.dll",
        "SharpClaw.Modules.ModuleDev.dll",
        "SharpClaw.Modules.Providers.Anthropic.dll",
        "SharpClaw.Modules.Providers.Google.dll",
        "SharpClaw.Modules.Providers.LlamaSharp.dll",
        "SharpClaw.Modules.Providers.Ollama.dll",
        "SharpClaw.Modules.Providers.OpenAICompatible.dll",
        "SharpClaw.Modules.TestHarness.dll",
        "SharpClaw.Modules.VS2026Editor.dll",
        "SharpClaw.Modules.VSCodeEditor.dll",
    ];

    private static readonly Dictionary<string, string[]> ExpectedBlockerKeys = new(StringComparer.Ordinal)
    {
        ["sharpclaw_agent_orchestration"] =
        [
            "events.sinks",
            "module.cli_commands",
            "module.global_flags",
            "module.header_tags",
            "module.resource_descriptors",
            "storage.module_dbcontexts",
            "tasks.parser_extension",
            "tasks.runtime_services",
        ],
        ["sharpclaw_editor_common"] =
        [
            "contracts.clr.exports",
            "module.cli_commands",
            "module.header_tags",
            "storage.module_dbcontexts",
        ],
        ["sharpclaw_metrics"] =
        [
            "tasks.parser_extension",
            "tasks.runtime_services",
        ],
        ["sharpclaw_module_dev"] =
        [
            "contracts.clr.requirements",
            "module.cli_commands",
            "module.global_flags",
        ],
        ["sharpclaw_providers_anthropic"] =
        [
            "providers.plugins",
        ],
        ["sharpclaw_providers_google"] =
        [
            "providers.plugins",
        ],
        ["sharpclaw_providers_llamasharp"] =
        [
            "module.cli_commands",
            "module.frontend_contributions",
            "providers.plugins",
            "storage.module_dbcontexts",
        ],
        ["sharpclaw_providers_ollama"] =
        [
            "providers.plugins",
        ],
        ["sharpclaw_providers_openai_compat"] =
        [
            "providers.plugins",
        ],
        ["sharpclaw_test_harness"] =
        [
            "jobs.completion_behavior",
            "module.global_flags",
            "module.header_tags",
            "module.resource_descriptors",
            "providers.plugins",
        ],
        ["sharpclaw_vs2026_editor"] =
        [
            "contracts.clr.requirements",
            "module.resource_descriptors",
        ],
        ["sharpclaw_vscode_editor"] =
        [
            "contracts.clr.requirements",
        ],
    };

    [Test]
    public void BundledSidecarReadinessInventoryIncludesEveryBundledModule()
    {
        var reports = AnalyzeBundledModules();

        reports.Select(report => report.ModuleId)
            .Should()
            .Equal(ExpectedBlockerKeys.Keys.Order(StringComparer.Ordinal));

        reports.Should().OnlyContain(report => !string.IsNullOrWhiteSpace(report.ModuleType));
        reports.Should().OnlyContain(report => !string.IsNullOrWhiteSpace(report.AssemblyName));
    }

    [Test]
    public void BundledSidecarReadinessInventoryCapturesKnownProtocolGaps()
    {
        var reports = AnalyzeBundledModules();
        var expected = ExpectedBlockerKeys.ToDictionary(
            pair => pair.Key,
            pair => string.Join("|", pair.Value.Order(StringComparer.Ordinal)),
            StringComparer.Ordinal);
        var actual = reports.ToDictionary(
            report => report.ModuleId,
            report => string.Join("|", report.Blockers.Select(finding => finding.Key).Order(StringComparer.Ordinal)),
            StringComparer.Ordinal);

        actual.Should().Equal(expected);
        reports.Should().OnlyContain(report => !report.IsReadyForSidecarDefault);
    }

    [Test]
    public void BundledSidecarReadinessInventoryDistinguishesCoveredProtocolSurfaces()
    {
        var reports = AnalyzeBundledModules().ToDictionary(report => report.ModuleId, StringComparer.Ordinal);

        reports["sharpclaw_agent_orchestration"].Findings
            .Should()
            .Contain(finding => finding.Kind == SidecarReadinessFindingKind.CoveredByCurrentProtocol
                                && finding.Key == "tools.job");

        reports["sharpclaw_test_harness"].Findings
            .Should()
            .Contain(finding => finding.Kind == SidecarReadinessFindingKind.CoveredByCurrentProtocol
                                && finding.Key == "tools.inline")
            .And
            .Contain(finding => finding.Kind == SidecarReadinessFindingKind.CoveredByCurrentProtocol
                                && finding.Key == "tools.job");

        reports["sharpclaw_editor_common"].Findings
            .Should()
            .Contain(finding => finding.Kind == SidecarReadinessFindingKind.CoveredByCurrentProtocol
                                && finding.Key == "endpoints.http");
    }

    private static IReadOnlyList<ModuleSidecarReadinessReport> AnalyzeBundledModules()
    {
        var modules = LoadBundledModules();
        var analyzer = new SidecarReadinessAnalyzer();
        return analyzer.AnalyzeAll(modules);
    }

    private static IReadOnlyList<ISharpClawModule> LoadBundledModules()
    {
        var apiOutputDir = ResolveApiOutputDirectory();
        var moduleType = typeof(ISharpClawModule);
        var modules = new List<ISharpClawModule>();

        foreach (var dllName in ExpectedModuleDlls)
        {
            var dllPath = Path.Combine(apiOutputDir, dllName);
            File.Exists(dllPath).Should().BeTrue($"'{dllName}' must be present in API output");

            var assembly = Assembly.LoadFrom(dllPath);
            var implementations = assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false }
                               && moduleType.IsAssignableFrom(type)
                               && type.GetConstructor(Type.EmptyTypes) is not null)
                .ToList();

            implementations.Should().ContainSingle(
                $"'{dllName}' must contain exactly one public parameterless ISharpClawModule implementation");

            modules.Add((ISharpClawModule)Activator.CreateInstance(implementations[0])!);
        }

        return modules.OrderBy(module => module.Id, StringComparer.Ordinal).ToArray();
    }

    private static string ResolveApiOutputDirectory()
    {
        var testBinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var solutionRoot = Path.GetFullPath(Path.Combine(testBinDir, "..", "..", "..", ".."));
        var config = new DirectoryInfo(testBinDir).Parent!.Name;
        var tfm = new DirectoryInfo(testBinDir).Name;

        return Path.Combine(solutionRoot, "SharpClaw.Application.API", "bin", config, tfm);
    }
}
