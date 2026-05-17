using NUnit.Framework;

namespace SharpClaw.Tests.TestHarness;

internal static class HarnessTestCategories
{
    public const string PerformanceDiagnostic = "PerformanceDiagnostic";
    public const string PerformanceGate = "PerformanceGate";
}

internal static class HarnessDiagnostics
{
    public const string EnableEnvironmentVariable = "SHARPCLAW_RUN_PERF_DIAGNOSTICS";

    public static void RequireEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnableEnvironmentVariable),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                $"{HarnessTestCategories.PerformanceDiagnostic} tests are diagnostic targets. " +
                $"Set {EnableEnvironmentVariable}=1 to run them.");
        }
    }
}
