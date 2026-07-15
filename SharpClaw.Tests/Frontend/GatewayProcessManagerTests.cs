using FluentAssertions;
using SharpClaw.Services;
using SharpClaw.Shared.Instances;
using SharpClaw.Shared.Logging;

namespace SharpClaw.Tests.Frontend;

[TestFixture]
public class GatewayProcessManagerTests
{
    [Test]
    public void BundledGatewayInstanceRoot_WhenFrontendInstanceProvided_ResolvesUnderFrontendStack()
    {
        var instanceRoot = CreateTempDirectory();
        var sharedRoot = CreateTempDirectory();
        var logsRoot = CreateTempDirectory();
        using var processLogs = CreateProcessLogs(logsRoot);

        try
        {
            var frontendInstance = new FrontendInstanceService(
                explicitInstanceRoot: instanceRoot,
                sharedRootOverride: sharedRoot);

            var manager = new GatewayProcessManager(
                GatewayProcessManager.DefaultGatewayUrl,
                "http://127.0.0.1:48923",
                processLogs,
                frontendInstance);

            manager.BundledGatewayInstanceRoot.Should().Be(
                Path.Combine(frontendInstance.Paths.InstanceRoot, "stack", "gateway"));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceRoot);
            DeleteDirectoryIfExists(sharedRoot);
            DeleteDirectoryIfExists(logsRoot);
        }
    }

    [Test]
    public void UpdateBackendBaseUrl_WhenCalled_UpdatesForwardedBackendBaseUrl()
    {
        var logsRoot = CreateTempDirectory();
        try
        {
            using var processLogs = CreateProcessLogs(logsRoot);
            var manager = new GatewayProcessManager(
                GatewayProcessManager.DefaultGatewayUrl,
                "http://127.0.0.1:48923",
                processLogs);

            manager.UpdateBackendBaseUrl("http://127.0.0.1:48925");

            manager.BackendBaseUrl.Should().Be("http://127.0.0.1:48925");
        }
        finally
        {
            DeleteDirectoryIfExists(logsRoot);
        }
    }

    private static DurableProcessLogWriter CreateProcessLogs(string root) =>
        new(
            "gateway-tests",
            new SharpClawInstancePaths(
                SharpClawInstanceKind.Gateway,
                root,
                root,
                root),
            TimeSpan.FromHours(1));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpClaw.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
