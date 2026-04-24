using FluentAssertions;
using NUnit.Framework;
using SharpClaw.Application.Infrastructure.Tasks;
using SharpClaw.Application.Infrastructure.Tasks.Models;
using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Tests.Tasks;

/// <summary>
/// Tests for trigger-definition attribute extraction by <see cref="TaskScriptEngine.Parse"/>.
/// Covers all self-registration trigger attributes, concurrency policy, and diagnostic codes.
/// </summary>
[TestFixture]
public class TaskTriggerAttributeParserTests
{
    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static TaskScriptDefinition ParseOk(string source)
    {
        var result = TaskScriptEngine.Parse(source);
        result.Diagnostics.Where(d => d.Severity == TaskDiagnosticSeverity.Error)
            .Should().BeEmpty("source should parse without errors");
        return result.Definition!;
    }

    private static string Wrap(string classBody, string attributes = "") => $$"""
{{attributes}}
[Task("TriggerTask")]
public class TriggerTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        Log("ok");
    }
{{classBody}}
}
""";

    // ─────────────────────────────────────────────────────────────
    // No triggers
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_NoTriggerAttributes_ReturnsEmptyTriggerDefinitions()
    {
        var def = ParseOk(Wrap(""));
        def.TriggerDefinitions.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────
    // [Schedule] → Cron
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_Schedule_PopulatesCronKindAndExpression()
    {
        var def = ParseOk(Wrap("", """[Schedule("0 9 * * MON-FRI")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind           = TriggerKind.Cron,
                CronExpression = "0 9 * * MON-FRI",
                CronTimezone   = (string?)null,
            });
    }

    [Test]
    public void Parse_ScheduleWithTimezone_ExtractsCronTimezone()
    {
        var def = ParseOk(Wrap("", """[Schedule("0 9 * * *", Timezone = "America/New_York")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind           = TriggerKind.Cron,
                CronExpression = "0 9 * * *",
                CronTimezone   = "America/New_York",
            });
    }

    // ─────────────────────────────────────────────────────────────
    // [OnEvent] → Event
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnEvent_PopulatesEventKindAndType()
    {
        var def = ParseOk(Wrap("", """[OnEvent("ModelAdded")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind      = TriggerKind.Event,
                EventType = "ModelAdded",
            });
    }

    [Test]
    public void Parse_OnEventWithFilter_ExtractsFilter()
    {
        var def = ParseOk(Wrap("", """[OnEvent("ModelAdded", Filter = "provider=openai")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.Event,
                EventType   = "ModelAdded",
                EventFilter = "provider=openai",
            });
    }

    // ─────────────────────────────────────────────────────────────
    // [OnFileChanged] → FileChanged
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnFileChanged_PopulatesFileChangedKindAndPath()
    {
        var def = ParseOk(Wrap("", """[OnFileChanged("/tmp/data")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind      = TriggerKind.FileChanged,
                WatchPath = "/tmp/data",
                FileEvents = FileWatchEvent.Any,
            });
    }

    [Test]
    public void Parse_OnFileChangedWithPatternAndEvents_ExtractsNamedArgs()
    {
        var def = ParseOk(Wrap("", """[OnFileChanged("/tmp/data", Pattern = "*.json", Events = FileWatchEvent.Created | FileWatchEvent.Deleted)]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.FilePattern.Should().Be("*.json");
        t.FileEvents.Should().Be(FileWatchEvent.Created | FileWatchEvent.Deleted);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnProcessStarted] / [OnProcessStopped] → Process*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnProcessStarted_PopulatesProcessStartedKind()
    {
        var def = ParseOk(Wrap("", """[OnProcessStarted("chrome")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.ProcessStarted,
                ProcessName = "chrome",
            });
    }

    [Test]
    public void Parse_OnProcessStopped_PopulatesProcessStoppedKind()
    {
        var def = ParseOk(Wrap("", """[OnProcessStopped("chrome")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.ProcessStopped,
                ProcessName = "chrome",
            });
    }

    // ─────────────────────────────────────────────────────────────
    // [OnWebhook] → Webhook
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnWebhook_PopulatesWebhookKindAndRoute()
    {
        var def = ParseOk(Wrap("", """[OnWebhook("/hook/deploy")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind         = TriggerKind.Webhook,
                WebhookRoute = "/hook/deploy",
            });
    }

    [Test]
    public void Parse_OnWebhookWithSecret_ExtractsSecretAndSignatureHeader()
    {
        var def = ParseOk(Wrap("", """[OnWebhook("/hook/deploy", Secret = "GH_SECRET", SignatureHeader = "X-Hub-Signature-256")]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.WebhookSecretEnvVar.Should().Be("GH_SECRET");
        t.WebhookSignatureHeader.Should().Be("X-Hub-Signature-256");
    }

    // ─────────────────────────────────────────────────────────────
    // [OnHostReachable] / [OnHostUnreachable] → Host*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnHostReachable_PopulatesHostKindAndName()
    {
        var def = ParseOk(Wrap("", """[OnHostReachable("db.internal")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind     = TriggerKind.HostReachable,
                HostName = "db.internal",
            });
    }

    [Test]
    public void Parse_OnHostUnreachableWithPort_ExtractsPort()
    {
        var def = ParseOk(Wrap("", """[OnHostUnreachable("db.internal", Port = 5432)]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.Kind.Should().Be(TriggerKind.HostUnreachable);
        t.HostPort.Should().Be(5432);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnTaskCompleted] / [OnTaskFailed] → Task*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnTaskCompleted_PopulatesSourceTaskName()
    {
        var def = ParseOk(Wrap("", """[OnTaskCompleted("IngestData")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind           = TriggerKind.TaskCompleted,
                SourceTaskName = "IngestData",
            });
    }

    [Test]
    public void Parse_OnTaskFailed_PopulatesSourceTaskName()
    {
        var def = ParseOk(Wrap("", """[OnTaskFailed("IngestData")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind           = TriggerKind.TaskFailed,
                SourceTaskName = "IngestData",
            });
    }

    // ─────────────────────────────────────────────────────────────
    // [OnWindowFocused] / [OnWindowBlurred] → Window*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnWindowFocused_PopulatesWindowFocusedKind()
    {
        var def = ParseOk(Wrap("", """[OnWindowFocused("notepad")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.WindowFocused,
                ProcessName = "notepad",
            });
    }

    [Test]
    public void Parse_OnWindowBlurred_PopulatesWindowBlurredKind()
    {
        var def = ParseOk(Wrap("", """[OnWindowBlurred("notepad")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.WindowBlurred,
                ProcessName = "notepad",
            });
    }

    // ─────────────────────────────────────────────────────────────
    // [OnHotkey] → Hotkey + TASK429
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnHotkeyValidCombo_PopulatesHotkeyKind()
    {
        var def = ParseOk(Wrap("", """[OnHotkey("Ctrl+Shift+F10")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.Hotkey,
                HotkeyCombo = "Ctrl+Shift+F10",
            });
    }

    [Test]
    public void Parse_OnHotkeyInvalidCombo_EmitsTask429()
    {
        var result = TaskScriptEngine.Parse(Wrap("", """[OnHotkey("BadCombo")]"""));
        result.Diagnostics.Should().Contain(d => d.Code == "TASK429");
    }

    [Test]
    public void Parse_OnHotkeyEmptyString_EmitsTask429()
    {
        var result = TaskScriptEngine.Parse(Wrap("", """[OnHotkey("")]"""));
        result.Diagnostics.Should().Contain(d => d.Code == "TASK429");
    }

    // ─────────────────────────────────────────────────────────────
    // [OnSystemIdle] / [OnSystemActive] → Idle/Active
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnSystemIdle_PopulatesIdleMinutes()
    {
        var def = ParseOk(Wrap("", """[OnSystemIdle(Minutes = 15)]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind        = TriggerKind.SystemIdle,
                IdleMinutes = 15,
            });
    }

    [Test]
    public void Parse_OnSystemActive_PopulatesSystemActiveKind()
    {
        var def = ParseOk(Wrap("", """[OnSystemActive]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.SystemActive);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnScreenLocked] / [OnScreenUnlocked] → Screen*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnScreenLocked_PopulatesScreenLockedKind()
    {
        var def = ParseOk(Wrap("", """[OnScreenLocked]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.ScreenLocked);
    }

    [Test]
    public void Parse_OnScreenUnlocked_PopulatesScreenUnlockedKind()
    {
        var def = ParseOk(Wrap("", """[OnScreenUnlocked]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.ScreenUnlocked);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnNetworkChanged] → NetworkChanged
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnNetworkChanged_PopulatesNetworkChangedKind()
    {
        var def = ParseOk(Wrap("", """[OnNetworkChanged]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.NetworkChanged);
    }

    [Test]
    public void Parse_OnNetworkChangedWithSsidAndState_ExtractsNamedArgs()
    {
        var def = ParseOk(Wrap("", """[OnNetworkChanged(Ssid = "HomeNet", State = NetworkState.Connected)]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.NetworkSsid.Should().Be("HomeNet");
        t.NetworkState.Should().Be(NetworkState.Connected);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnDeviceConnected] / [OnDeviceDisconnected] → Device*
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnDeviceConnected_PopulatesDeviceConnectedKind()
    {
        var def = ParseOk(Wrap("", """[OnDeviceConnected(Class = "USB", Pattern = "YubiKey*")]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.Kind.Should().Be(TriggerKind.DeviceConnected);
        t.DeviceClass.Should().Be("USB");
        t.DeviceNamePattern.Should().Be("YubiKey*");
    }

    [Test]
    public void Parse_OnDeviceDisconnected_PopulatesDeviceDisconnectedKind()
    {
        var def = ParseOk(Wrap("", """[OnDeviceDisconnected(Class = "USB")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.DeviceDisconnected);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnQueryReturnsRows] → QueryReturnsRows + TASK431
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnQueryReturnsRowsWithSelectCount_NoTask431()
    {
        var def = ParseOk(Wrap("", """[OnQueryReturnsRows("SELECT COUNT(*) FROM PendingItems WHERE Done = 0")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.QueryReturnsRows);
    }

    [Test]
    public void Parse_OnQueryReturnsRowsWithNonCountQuery_EmitsTask431()
    {
        var result = TaskScriptEngine.Parse(Wrap("", """[OnQueryReturnsRows("SELECT * FROM PendingItems")]"""));
        result.Diagnostics.Should().Contain(d => d.Code == "TASK431");
    }

    [Test]
    public void Parse_OnQueryReturnsRowsWithPollInterval_ExtractsPollInterval()
    {
        var def = ParseOk(Wrap("", """[OnQueryReturnsRows("SELECT COUNT(*) FROM Q", PollInterval = 30)]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.QueryPollIntervalSecs.Should().Be(30);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnMetricThreshold] → MetricThreshold
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnMetricThreshold_PopulatesMetricFields()
    {
        var def = ParseOk(Wrap("", """[OnMetricThreshold("System.CpuPercent", Threshold = 90.0, Direction = ThresholdDirection.Above)]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.Kind.Should().Be(TriggerKind.MetricThreshold);
        t.MetricSource.Should().Be("System.CpuPercent");
        t.MetricThreshold.Should().Be(90.0);
        t.MetricDirection.Should().Be(ThresholdDirection.Above);
    }

    // ─────────────────────────────────────────────────────────────
    // [OnStartup] / [OnShutdown] → Startup/Shutdown
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnStartup_PopulatesStartupKind()
    {
        var def = ParseOk(Wrap("", """[OnStartup]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.Startup);
    }

    [Test]
    public void Parse_OnShutdown_PopulatesShutdownKind()
    {
        var def = ParseOk(Wrap("", """[OnShutdown]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Kind.Should().Be(TriggerKind.Shutdown);
    }

    // ─────────────────────────────────────────────────────────────
    // [OsShortcut] → OsShortcut
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OsShortcut_PopulatesShortcutFields()
    {
        var def = ParseOk(Wrap("", """[OsShortcut("Run Ingest", Icon = "ingest.ico", Category = "Data")]"""));
        var t = def.TriggerDefinitions.Should().ContainSingle().Subject;
        t.Kind.Should().Be(TriggerKind.OsShortcut);
        t.ShortcutLabel.Should().Be("Run Ingest");
        t.ShortcutIcon.Should().Be("ingest.ico");
        t.ShortcutCategory.Should().Be("Data");
    }

    // ─────────────────────────────────────────────────────────────
    // [OnTrigger] → Custom
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_OnTrigger_PopulatesCustomKindAndSourceName()
    {
        var def = ParseOk(Wrap("", """[OnTrigger("MyCustomSource")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                Kind             = TriggerKind.Custom,
                CustomSourceName = "MyCustomSource",
            });
    }

    [Test]
    public void Parse_OnTriggerWithFilter_ExtractsFilter()
    {
        var def = ParseOk(Wrap("", """[OnTrigger("MyCustomSource", Filter = "type=foo")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.CustomSourceFilter.Should().Be("type=foo");
    }

    // ─────────────────────────────────────────────────────────────
    // [ConcurrencyPolicy] propagation
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_ConcurrencyPolicy_PropagatedToAllTriggers()
    {
        var source = Wrap("", """
[Schedule("0 9 * * *")]
[OnEvent("ModelAdded")]
[ConcurrencyPolicy(Policy = TriggerConcurrency.Queue)]
""");
        var def = ParseOk(source);
        def.TriggerDefinitions.Should().HaveCount(2);
        def.TriggerDefinitions.Should().AllSatisfy(t => t.Concurrency.Should().Be(TriggerConcurrency.Queue));
    }

    [Test]
    public void Parse_NoConcurrencyPolicy_DefaultsToSkipIfRunning()
    {
        var def = ParseOk(Wrap("", """[Schedule("0 9 * * *")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Concurrency.Should().Be(TriggerConcurrency.SkipIfRunning);
    }

    // ─────────────────────────────────────────────────────────────
    // TASK421 — multiple [ConcurrencyPolicy]
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_MultipleConcurrencyPolicyAttributes_EmitsTask421()
    {
        var source = Wrap("", """
[Schedule("0 9 * * *")]
[ConcurrencyPolicy(Policy = TriggerConcurrency.Queue)]
[ConcurrencyPolicy(Policy = TriggerConcurrency.Parallel)]
""");
        var result = TaskScriptEngine.Parse(source);
        result.Diagnostics.Should().Contain(d => d.Code == "TASK421");
    }

    // ─────────────────────────────────────────────────────────────
    // TASK428 — [WebhookSecret] without [OnWebhook]
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_WebhookSecretWithoutOnWebhook_EmitsTask428()
    {
        var result = TaskScriptEngine.Parse(Wrap("", """[WebhookSecret("MY_SECRET")]"""));
        result.Diagnostics.Should().Contain(d => d.Code == "TASK428");
    }

    [Test]
    public void Parse_WebhookSecretWithOnWebhook_NoTask428()
    {
        var result = TaskScriptEngine.Parse(Wrap("", """
[OnWebhook("/hook/deploy")]
[WebhookSecret("MY_SECRET")]
"""));
        result.Diagnostics.Should().NotContain(d => d.Code == "TASK428");
    }

    // ─────────────────────────────────────────────────────────────
    // Multiple triggers on same class
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_MultipleTriggerAttributes_AllExtracted()
    {
        var source = Wrap("", """
[Schedule("0 * * * *")]
[OnEvent("ModelAdded")]
[OnStartup]
""");
        var def = ParseOk(source);
        def.TriggerDefinitions.Should().HaveCount(3);
        def.TriggerDefinitions.Select(t => t.Kind)
            .Should().BeEquivalentTo([TriggerKind.Cron, TriggerKind.Event, TriggerKind.Startup]);
    }

    // ─────────────────────────────────────────────────────────────
    // Line numbers are captured
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Parse_TriggerDefinition_RecordsNonZeroLineNumber()
    {
        var def = ParseOk(Wrap("", """[Schedule("0 9 * * *")]"""));
        def.TriggerDefinitions.Should().ContainSingle()
            .Which.Line.Should().BeGreaterThan(0);
    }
}
