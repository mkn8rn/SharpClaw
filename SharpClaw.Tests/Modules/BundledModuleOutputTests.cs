using System.Reflection;

namespace SharpClaw.Tests.Modules;

/// <summary>
/// Verifies that all bundled module DLLs are present in the API project output directory.
/// This test will fail if the CopyBundledModules MSBuild target in
/// SharpClaw.Application.API.csproj is broken or a module project is removed.
/// </summary>
[TestFixture]
public class BundledModuleOutputTests
{
    private static readonly string[] ExpectedModuleDlls =
    [
        "SharpClaw.Modules.AgentOrchestration.dll",
        "SharpClaw.Modules.BotIntegration.dll",
        "SharpClaw.Modules.ComputerUse.dll",
        "SharpClaw.Modules.ContextTools.dll",
        "SharpClaw.Modules.DangerousShell.dll",
        "SharpClaw.Modules.DatabaseAccess.dll",
        "SharpClaw.Modules.EditorCommon.dll",
        "SharpClaw.Modules.Mk8Shell.dll",
        "SharpClaw.Modules.ModuleDev.dll",
        "SharpClaw.Modules.OfficeApps.dll",
        "SharpClaw.Modules.Transcription.dll",
        "SharpClaw.Modules.VS2026Editor.dll",
        "SharpClaw.Modules.VSCodeEditor.dll",
        "SharpClaw.Modules.WebAccess.dll",
    ];

    /// <summary>
    /// Resolves the API project output directory by walking up from the test assembly
    /// location and finding the sibling API bin folder at the same configuration/TFM.
    /// </summary>
    private static string ResolveApiOutputDirectory()
    {
        // Test assembly lives at: …/SharpClaw.Tests/bin/<config>/net10.0/
        // API output lives at:    …/SharpClaw.Application.API/bin/<config>/net10.0/
        var testBinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // Walk up to the solution root (four levels: net10.0 → <config> → bin → project)
        var solutionRoot = Path.GetFullPath(Path.Combine(testBinDir, "..", "..", "..", ".."));
        var config = new DirectoryInfo(testBinDir).Parent!.Name; // Debug or Release
        var tfm = new DirectoryInfo(testBinDir).Name;            // net10.0

        return Path.Combine(solutionRoot, "SharpClaw.Application.API", "bin", config, tfm);
    }

    [Test]
    public void AllBundledModuleDllsArePresentInApiOutput()
    {
        var apiOutputDir = ResolveApiOutputDirectory();

        apiOutputDir.Should().NotBeNull();
        Directory.Exists(apiOutputDir).Should().BeTrue(
            $"API output directory should exist at '{apiOutputDir}'");

        var missing = ExpectedModuleDlls
            .Where(dll => !File.Exists(Path.Combine(apiOutputDir, dll)))
            .ToList();

        missing.Should().BeEmpty(
            $"the following module DLLs were not found in '{apiOutputDir}': {string.Join(", ", missing)}");
    }

    [TestCaseSource(nameof(ExpectedModuleDlls))]
    public void ModuleDllContainsISharpClawModuleImplementation(string dllName)
    {
        var apiOutputDir = ResolveApiOutputDirectory();
        var dllPath = Path.Combine(apiOutputDir, dllName);

        File.Exists(dllPath).Should().BeTrue($"'{dllName}' must be present in API output");

        var assembly = Assembly.LoadFrom(dllPath);
        var moduleType = Type.GetType(
            "SharpClaw.Contracts.Modules.ISharpClawModule, SharpClaw.Contracts",
            throwOnError: false);

        moduleType.Should().NotBeNull("SharpClaw.Contracts must be loaded");

        var implementations = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && moduleType!.IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        implementations.Should().NotBeEmpty(
            $"'{dllName}' must contain at least one public parameterless ISharpClawModule implementation");
    }
}
