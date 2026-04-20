using System.Text;
using System.Text.Json;
using SharpClaw.Infrastructure.Persistence.JSON;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public class TwoPhaseCommitTests
{
    private InMemoryPersistenceFileSystem _fs = null!;

    [SetUp]
    public void SetUp()
    {
        _fs = new InMemoryPersistenceFileSystem();
        _fs.CreateDirectory("/data");
        _fs.CreateDirectory("/data/EntityA");
    }

    // ── Happy path ───────────────────────────────────────────────

    [Test]
    public async Task Commit_WritesAllFilesAtomically()
    {
        var tpc = new TwoPhaseCommit(_fs, fsync: false);
        await tpc.StageAsync("/data/EntityA/1.json", "hello"u8.ToArray(), default);
        await tpc.StageAsync("/data/EntityA/2.json", "world"u8.ToArray(), default);

        await tpc.CommitAsync("/data/EntityA", default);

        (await _fs.ReadAllTextAsync("/data/EntityA/1.json")).Should().Be("hello");
        (await _fs.ReadAllTextAsync("/data/EntityA/2.json")).Should().Be("world");
        // No .tmp files remain.
        _fs.FileExists("/data/EntityA/1.json.tmp").Should().BeFalse();
        _fs.FileExists("/data/EntityA/2.json.tmp").Should().BeFalse();
        // Marker removed.
        _fs.FileExists("/data/EntityA/_commit.marker").Should().BeFalse();
    }

    [Test]
    public async Task Commit_WithDelete_RemovesFile()
    {
        await _fs.WriteAllTextAsync("/data/EntityA/old.json", "stale", default);
        var tpc = new TwoPhaseCommit(_fs, fsync: false);
        tpc.StageDelete("/data/EntityA/old.json");
        await tpc.StageAsync("/data/EntityA/new.json", "fresh"u8.ToArray(), default);

        await tpc.CommitAsync("/data/EntityA", default);

        _fs.FileExists("/data/EntityA/old.json").Should().BeFalse();
        (await _fs.ReadAllTextAsync("/data/EntityA/new.json")).Should().Be("fresh");
    }

    [Test]
    public async Task Commit_EmptyStaged_IsNoOp()
    {
        var tpc = new TwoPhaseCommit(_fs, fsync: false);
        await tpc.CommitAsync("/data/EntityA", default);
        _fs.FileExists("/data/EntityA/_commit.marker").Should().BeFalse();
    }

    // ── Rollback: failure before marker ──────────────────────────

    [Test]
    public async Task Failure_BeforeMarker_RollsBackTmpFiles()
    {
        // Inject a fault on the marker write.
        var faultFs = new InMemoryPersistenceFileSystem();
        faultFs.CreateDirectory("/data/EntityA");
        var markerPath = faultFs.CombinePath("/data/EntityA", TwoPhaseCommit.CommitMarkerFileName);

        faultFs.OnBeforeWrite = path =>
        {
            if (path == markerPath)
                throw new IOException("Simulated disk failure on marker write");
            return Task.CompletedTask;
        };

        var tpc = new TwoPhaseCommit(faultFs, fsync: false);
        await tpc.StageAsync("/data/EntityA/1.json", "data"u8.ToArray(), default);

        var act = () => tpc.CommitAsync("/data/EntityA", default);
        await act.Should().ThrowAsync<IOException>();

        // .tmp files should be cleaned up (rollback).
        faultFs.FileExists("/data/EntityA/1.json.tmp").Should().BeFalse();
        // Final file should NOT exist (never renamed).
        faultFs.FileExists("/data/EntityA/1.json").Should().BeFalse();
    }

    // ── Roll forward: failure after marker ───────────────────────

    [Test]
    public async Task Failure_AfterMarker_LeavesMarkerForRecovery()
    {
        // Stage files, then simulate crash after marker but before rename completes.
        var tpc = new TwoPhaseCommit(_fs, fsync: false);
        await tpc.StageAsync("/data/EntityA/1.json", "data1"u8.ToArray(), default);
        await tpc.StageAsync("/data/EntityA/2.json", "data2"u8.ToArray(), default);

        // Manually write the marker to simulate partial commit.
        var markerPath = _fs.CombinePath("/data/EntityA", TwoPhaseCommit.CommitMarkerFileName);
        // Write marker manually before the .tmp files would normally be renamed.
        var markerEntries = new[]
        {
            new { TmpPath = "/data/EntityA/1.json.tmp", FinalPath = "/data/EntityA/1.json", IsDelete = false },
            new { TmpPath = "/data/EntityA/2.json.tmp", FinalPath = "/data/EntityA/2.json", IsDelete = false }
        };
        var markerJson = JsonSerializer.Serialize(markerEntries);
        await _fs.WriteAllTextAsync(markerPath, markerJson, default);

        // .tmp files exist (staged), finals do not.
        _fs.FileExists("/data/EntityA/1.json.tmp").Should().BeTrue();
        _fs.FileExists("/data/EntityA/1.json").Should().BeFalse();

        // Recovery should roll forward.
        await TwoPhaseCommit.RecoverAsync(_fs, markerPath, default);

        (await _fs.ReadAllTextAsync("/data/EntityA/1.json")).Should().Be("data1");
        (await _fs.ReadAllTextAsync("/data/EntityA/2.json")).Should().Be("data2");
        _fs.FileExists(markerPath).Should().BeFalse();
    }

    [Test]
    public async Task Recovery_PartiallyCompleted_SkipsAlreadyRenamed()
    {
        // Simulate: 1.json already renamed, 2.json still .tmp.
        await _fs.WriteAllBytesAsync("/data/EntityA/1.json", "done"u8.ToArray(), default);
        await _fs.WriteAllBytesAsync("/data/EntityA/2.json.tmp", "pending"u8.ToArray(), default);

        var markerEntries = new[]
        {
            new { TmpPath = "/data/EntityA/1.json.tmp", FinalPath = "/data/EntityA/1.json", IsDelete = false },
            new { TmpPath = "/data/EntityA/2.json.tmp", FinalPath = "/data/EntityA/2.json", IsDelete = false }
        };
        var markerPath = "/data/EntityA/_commit.marker";
        await _fs.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(markerEntries), default);

        await TwoPhaseCommit.RecoverAsync(_fs, markerPath, default);

        (await _fs.ReadAllTextAsync("/data/EntityA/1.json")).Should().Be("done"); // untouched
        (await _fs.ReadAllTextAsync("/data/EntityA/2.json")).Should().Be("pending"); // rolled forward
        _fs.FileExists(markerPath).Should().BeFalse();
    }

    // ── RecoverAll scans subdirectories ──────────────────────────

    [Test]
    public async Task RecoverAll_ScansAllSubdirectories()
    {
        _fs.CreateDirectory("/data/TypeA");
        _fs.CreateDirectory("/data/TypeB");

        // Leave markers in two dirs.
        await LeaveMarkerAsync("/data/TypeA", "/data/TypeA/a.json", "aaa");
        await LeaveMarkerAsync("/data/TypeB", "/data/TypeB/b.json", "bbb");

        await TwoPhaseCommit.RecoverAllAsync(_fs, "/data", default);

        (await _fs.ReadAllTextAsync("/data/TypeA/a.json")).Should().Be("aaa");
        (await _fs.ReadAllTextAsync("/data/TypeB/b.json")).Should().Be("bbb");
        _fs.FileExists("/data/TypeA/_commit.marker").Should().BeFalse();
        _fs.FileExists("/data/TypeB/_commit.marker").Should().BeFalse();
    }

    // ── Crash-loop recovery (idempotent) ─────────────────────────

    [Test]
    public async Task Recovery_IsIdempotent_MultipleRuns()
    {
        await _fs.WriteAllBytesAsync("/data/EntityA/x.json.tmp", "val"u8.ToArray(), default);

        var markerEntries = new[]
        {
            new { TmpPath = "/data/EntityA/x.json.tmp", FinalPath = "/data/EntityA/x.json", IsDelete = false }
        };
        var markerPath = "/data/EntityA/_commit.marker";
        await _fs.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(markerEntries), default);

        // Run recovery twice.
        await TwoPhaseCommit.RecoverAsync(_fs, markerPath, default);
        // Marker is gone, but run again should be no-op.
        await TwoPhaseCommit.RecoverAsync(_fs, markerPath, default);

        (await _fs.ReadAllTextAsync("/data/EntityA/x.json")).Should().Be("val");
    }

    // ── Join table in two-phase envelope ─────────────────────────

    [Test]
    public async Task Commit_IncludesJoinTableRows()
    {
        _fs.CreateDirectory("/data/JoinTable");
        var tpc = new TwoPhaseCommit(_fs, fsync: false);
        await tpc.StageAsync("/data/EntityA/1.json", "entity"u8.ToArray(), default);
        await tpc.StageAsync("/data/JoinTable/_rows.json", "[{\"A\":\"b\"}]"u8.ToArray(), default);

        await tpc.CommitAsync("/data", default);

        (await _fs.ReadAllTextAsync("/data/EntityA/1.json")).Should().Be("entity");
        (await _fs.ReadAllTextAsync("/data/JoinTable/_rows.json")).Should().Contain("A");
        _fs.FileExists("/data/_commit.marker").Should().BeFalse();
    }

    // ── Helper ───────────────────────────────────────────────────

    private async Task LeaveMarkerAsync(string dir, string finalPath, string content)
    {
        var tmpPath = finalPath + ".tmp";
        await _fs.WriteAllBytesAsync(tmpPath, Encoding.UTF8.GetBytes(content), default);
        var markerEntries = new[]
        {
            new { TmpPath = tmpPath, FinalPath = finalPath, IsDelete = false }
        };
        var markerPath = _fs.CombinePath(dir, TwoPhaseCommit.CommitMarkerFileName);
        await _fs.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(markerEntries), default);
    }
}
