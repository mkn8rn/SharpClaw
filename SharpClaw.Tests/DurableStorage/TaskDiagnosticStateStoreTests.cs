using System.Security.Cryptography;
using SharpClaw.Core.Tasks.Runtime;
using SharpClaw.Runtime.INF.DurableStorage;

namespace SharpClaw.Tests.DurableStorage;

[TestFixture]
public sealed class TaskDiagnosticStateStoreTests
{
    private readonly List<string> _roots = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var root in _roots)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        _roots.Clear();
    }

    [Test]
    public async Task ApplyChangeAsync_ExternalizesOnlyTheChangedBigEntry()
    {
        var (stateStore, artifacts, _) = CreateStores();
        var instanceId = Guid.NewGuid();
        var first = new BigDataEntry(
            "first",
            "First",
            new string('a', 50_000),
            DateTimeOffset.UtcNow);
        var second = new BigDataEntry(
            "second",
            "Second",
            new string('b', 60_000),
            DateTimeOffset.UtcNow);

        await stateStore.ApplyChangeAsync(
            instanceId,
            new TaskDiagnosticStateChange(
                TaskDiagnosticStateChangeKind.LightDataReplaced,
                "bounded light state"));
        await stateStore.ApplyChangeAsync(
            instanceId,
            BigDataChange(first));
        await stateStore.ApplyChangeAsync(
            instanceId,
            BigDataChange(second));

        var before = await stateStore.ReadAsync(instanceId);
        var firstArtifact = before!.BigData.Single(entry => entry.Id == "first").Artifact.Id;
        artifacts.GetSnapshot().ArtifactCount.Should().Be(2);

        await stateStore.ApplyChangeAsync(
            instanceId,
            BigDataChange(first with { Content = "replacement" }));

        var after = await stateStore.ReadAsync(instanceId);
        after!.LightData.Should().Be("bounded light state");
        after.BigData.Select(entry => entry.Id).Should().Equal("first", "second");
        after.BigData.Single(entry => entry.Id == "first").Artifact.Id
            .Should().NotBe(firstArtifact);
        after.BigData.Single(entry => entry.Id == "second").Artifact.Id
            .Should().Be(before.BigData.Single(entry => entry.Id == "second").Artifact.Id);
        artifacts.GetSnapshot().ArtifactCount.Should().Be(3);
    }

    [Test]
    public async Task ReadAsync_BindsEncryptedManifestToItsTaskInstance()
    {
        var (stateStore, _, root) = CreateStores();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await stateStore.ApplyChangeAsync(
            sourceId,
            new TaskDiagnosticStateChange(
                TaskDiagnosticStateChangeKind.LightDataReplaced,
                "protected"));
        var source = Path.Combine(root, "task-state", $"{sourceId:D}.scstate");
        var target = Path.Combine(root, "task-state", $"{targetId:D}.scstate");
        File.Copy(source, target);

        Func<Task> read = async () => _ = await stateStore.ReadAsync(targetId);

        await read.Should().ThrowAsync<CryptographicException>();
    }

    [Test]
    public async Task Retention_ProtectsActiveTaskStateAndExpiresTerminalState()
    {
        var (stateStore, _, root) = CreateStores();
        var protectedId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();
        foreach (var instanceId in new[] { protectedId, expiredId })
        {
            await stateStore.ApplyChangeAsync(
                instanceId,
                new TaskDiagnosticStateChange(
                    TaskDiagnosticStateChangeKind.LightDataReplaced,
                    "state"));
            File.SetLastWriteTimeUtc(
                Path.Combine(root, "task-state", $"{instanceId:D}.scstate"),
                DateTime.UtcNow.AddDays(-10));
        }

        var result = await stateStore.ApplyRetentionAsync(
            new HashSet<Guid> { protectedId },
            TimeSpan.FromDays(1),
            maximumDeletes: 10);

        result.DeletedStates.Should().Be(1);
        (await stateStore.ReadAsync(protectedId)).Should().NotBeNull();
        (await stateStore.ReadAsync(expiredId)).Should().BeNull();
    }

    private (TaskDiagnosticStateStore States, ExecutionArtifactStore Artifacts, string Root)
        CreateStores()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpClaw.Tests",
            "task-state",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        var artifactKey = RandomNumberGenerator.GetBytes(32);
        var artifacts = new ExecutionArtifactStore(root, artifactKey);
        var states = new TaskDiagnosticStateStore(
            root,
            RandomNumberGenerator.GetBytes(32),
            artifacts);
        return (states, artifacts, root);
    }

    private static TaskDiagnosticStateChange BigDataChange(BigDataEntry entry) =>
        new(
            TaskDiagnosticStateChangeKind.BigDataUpserted,
            BigData: new TaskDiagnosticBigDataChange(
                entry.Id,
                entry.Title,
                entry.Content,
                entry.CreatedAt));
}
