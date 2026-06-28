using FluentAssertions;
using NUnit.Framework;
using SharpClaw.Core.Tasks;
using SharpClaw.Core.Tasks.Models;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Modules.AgentOrchestration;
using SharpClaw.Modules.Metrics;

namespace SharpClaw.Tests.Tasks;

/// <summary>
/// Parser tests for trigger attributes owned by modules that remain bundled
/// with the core repository.
/// </summary>
[TestFixture]
public class TaskTriggerAttributeParserTests
{
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

    private static TaskTriggerDefinition Single(string source) =>
        ParseOk(source).TriggerDefinitions.Should().ContainSingle().Subject;

    [Test]
    public void Parse_NoTriggerAttributes_ReturnsEmptyTriggerDefinitions()
    {
        ParseOk(Wrap("")).TriggerDefinitions.Should().BeEmpty();
    }

    [Test]
    public void Parse_Schedule_PopulatesCronTriggerKeyAndExpression()
    {
        var t = Single(Wrap("", """[Schedule("0 9 * * MON-FRI")]"""));
        t.TriggerKey.Should().Be(TaskScriptingTriggerKeys.Cron);
        t.Parameters[TaskScriptingTriggerKeys.CronExpression].Should().Be("0 9 * * MON-FRI");
        t.Parameters.ContainsKey(TaskScriptingTriggerKeys.CronTimezone).Should().BeFalse();
    }

    [Test]
    public void Parse_ScheduleWithTimezone_ExtractsCronTimezone()
    {
        var t = Single(Wrap("", """[Schedule("0 9 * * *", Timezone = "America/New_York")]"""));
        t.TriggerKey.Should().Be(TaskScriptingTriggerKeys.Cron);
        t.Parameters[TaskScriptingTriggerKeys.CronExpression].Should().Be("0 9 * * *");
        t.Parameters[TaskScriptingTriggerKeys.CronTimezone].Should().Be("America/New_York");
    }

    [Test]
    public void Parse_OnEvent_PopulatesEventTriggerKeyAndType()
    {
        var t = Single(Wrap("", """[OnEvent("ModelAdded")]"""));
        t.TriggerKey.Should().Be(AgentOrchestrationTriggerKeys.Event);
        t.Parameters[AgentOrchestrationTriggerKeys.EventType].Should().Be("ModelAdded");
    }

    [Test]
    public void Parse_OnEventWithFilter_ExtractsFilter()
    {
        var t = Single(Wrap("", """[OnEvent("ModelAdded", Filter = "provider=openai")]"""));
        t.Parameters[AgentOrchestrationTriggerKeys.EventType].Should().Be("ModelAdded");
        t.Parameters[AgentOrchestrationTriggerKeys.EventFilter].Should().Be("provider=openai");
    }

    [Test]
    public void Parse_OnFileChanged_PopulatesFileChangedTriggerKeyAndPath()
    {
        var t = Single(Wrap("", """[OnFileChanged("/tmp/data")]"""));
        t.TriggerKey.Should().Be(FilesystemTriggerKeys.FileChanged);
        t.Parameters[FilesystemTriggerKeys.WatchPath].Should().Be("/tmp/data");
        t.Parameters[FilesystemTriggerKeys.FileEvents].Should().Be(FileWatchEvent.Any.ToString());
    }

    [Test]
    public void Parse_OnFileChangedWithPatternAndEvents_ExtractsNamedArgs()
    {
        var t = Single(Wrap("", """[OnFileChanged("/tmp/data", Pattern = "*.json", Events = FileWatchEvent.Created | FileWatchEvent.Deleted)]"""));
        t.Parameters[FilesystemTriggerKeys.FilePattern].Should().Be("*.json");
        t.Parameters[FilesystemTriggerKeys.FileEvents].Should().Be((FileWatchEvent.Created | FileWatchEvent.Deleted).ToString());
    }

    [Test]
    public void Parse_OnTaskCompleted_PopulatesSourceTaskName()
    {
        var t = Single(Wrap("", """[OnTaskCompleted("IngestData")]"""));
        t.TriggerKey.Should().Be(TaskScriptingTriggerKeys.TaskCompleted);
        t.Parameters[TaskScriptingTriggerKeys.SourceTaskName].Should().Be("IngestData");
    }

    [Test]
    public void Parse_OnTaskFailed_PopulatesSourceTaskName()
    {
        var t = Single(Wrap("", """[OnTaskFailed("IngestData")]"""));
        t.TriggerKey.Should().Be(TaskScriptingTriggerKeys.TaskFailed);
        t.Parameters[TaskScriptingTriggerKeys.SourceTaskName].Should().Be("IngestData");
    }

    [Test]
    public void Parse_OnMetricThreshold_PopulatesMetricFields()
    {
        var t = Single(Wrap("", """[OnMetricThreshold("System.CpuPercent", Threshold = 90.0, Direction = ThresholdDirection.Above)]"""));
        t.TriggerKey.Should().Be(MetricTriggerKeys.MetricThreshold);
        t.Parameters[MetricTriggerKeys.Source].Should().Be("System.CpuPercent");
        t.Parameters[MetricTriggerKeys.Threshold].Should().Be("90");
        t.Parameters[MetricTriggerKeys.Direction].Should().Be(ThresholdDirection.Above.ToString());
    }

    [Test]
    public void Parse_OnStartup_PopulatesStartupTriggerKey()
    {
        Single(Wrap("", """[OnStartup]""")).TriggerKey
            .Should().Be(TaskScriptingTriggerKeys.Startup);
    }

    [Test]
    public void Parse_OnShutdown_PopulatesShutdownTriggerKey()
    {
        Single(Wrap("", """[OnShutdown]""")).TriggerKey
            .Should().Be(TaskScriptingTriggerKeys.Shutdown);
    }

    [Test]
    public void Parse_OnTrigger_PopulatesTriggerKey()
    {
        Single(Wrap("", """[OnTrigger("MyCustomSource")]""")).TriggerKey
            .Should().Be("MyCustomSource");
    }

    [Test]
    public void Parse_OnTriggerWithFilter_ExtractsFilter()
    {
        var t = Single(Wrap("", """[OnTrigger("MyCustomSource", Filter = "type=foo")]"""));
        t.Parameters[TaskScriptingTriggerKeys.CustomSourceFilter].Should().Be("type=foo");
    }

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
        def.TriggerDefinitions.Select(t => t.TriggerKey)
            .Should().BeEquivalentTo([
                TaskScriptingTriggerKeys.Cron,
                AgentOrchestrationTriggerKeys.Event,
                TaskScriptingTriggerKeys.Startup,
            ]);
    }

    [Test]
    public void Parse_TriggerDefinition_RecordsNonZeroLineNumber()
    {
        Single(Wrap("", """[Schedule("0 9 * * *")]""")).Line.Should().BeGreaterThan(0);
    }
}
