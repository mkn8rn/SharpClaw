using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

/// <summary>
/// Phase I tests: Snapshot checkpointing — ZIP creation, retention, restore, and quarantine fallback.
/// </summary>
[TestFixture]
public class SnapshotServiceTests
{
    private string _dataDir = null!;
    private IPersistenceFileSystem _fs = null!;
    private DirectoryLockManager _lockManager = null!;
    private ILogger<SnapshotService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"snapshot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDir);
        _fs = new PhysicalPersistenceFileSystem();
        _lockManager = new DirectoryLockManager();
        _logger = NullLogger<SnapshotService>.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        _lockManager.Dispose();
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private JsonFileOptions MakeOptions(bool enable = true, int retentionCount = 3)
        => new()
        {
            DataDirectory = _dataDir,
            EnableSnapshots = enable,
            SnapshotRetentionCount = retentionCount,
        };

    private SnapshotService CreateService(JsonFileOptions options)
        => new(_fs, options, _lockManager, _logger);

    private async Task WriteEntityFileAsync(string entityType, string fileName, string content)
    {
        var dir = Path.Combine(_dataDir, entityType);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), content);
    }

    // ── Tests ────────────────────────────────────────────────────

    [Test]
    public async Task CreateSnapshot_CreatesZipWithEntityFiles()
    {
        await WriteEntityFileAsync("Agent", "abc.json", """{"id":"abc"}""");
        await WriteEntityFileAsync("Model", "def.json", """{"id":"def"}""");

        var svc = CreateService(MakeOptions());
        var path = await svc.CreateSnapshotAsync(CancellationToken.None);

        path.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();

        using var zip = ZipFile.OpenRead(path!);
        zip.Entries.Should().HaveCount(2);
        zip.Entries.Select(e => e.FullName).Should().Contain("Agent/abc.json");
        zip.Entries.Select(e => e.FullName).Should().Contain("Model/def.json");
    }

    [Test]
    public async Task CreateSnapshot_SkipsInternalDirectoriesAndFiles()
    {
        await WriteEntityFileAsync("Agent", "abc.json", """{"id":"abc"}""");
        await WriteEntityFileAsync("Agent", "_checksums.json", "{}");

        // _events is an internal directory.
        var eventsDir = Path.Combine(_dataDir, "_events");
        Directory.CreateDirectory(eventsDir);
        await File.WriteAllTextAsync(Path.Combine(eventsDir, "events_20250101.jsonl"), "x");

        var svc = CreateService(MakeOptions());
        var path = await svc.CreateSnapshotAsync(CancellationToken.None);

        using var zip = ZipFile.OpenRead(path!);
        zip.Entries.Should().HaveCount(1);
        zip.Entries[0].FullName.Should().Be("Agent/abc.json");
    }

    [Test]
    public async Task CreateSnapshot_DisabledReturnsNull()
    {
        var svc = CreateService(MakeOptions(enable: false));
        var path = await svc.CreateSnapshotAsync(CancellationToken.None);
        path.Should().BeNull();
    }

    [Test]
    public async Task RetentionEnforcement_DeletesOldestBeyondCount()
    {
        var options = MakeOptions(retentionCount: 2);
        var svc = CreateService(options);
        var snapshotsDir = Path.Combine(_dataDir, "_snapshots");
        Directory.CreateDirectory(snapshotsDir);

        await WriteEntityFileAsync("Agent", "a.json", "{}");

        // Create 3 snapshots with distinct names manually to avoid timestamp collision.
        var snap1 = Path.Combine(snapshotsDir, "snapshot_20250101_000000.zip");
        var snap2 = Path.Combine(snapshotsDir, "snapshot_20250102_000000.zip");
        var snap3 = Path.Combine(snapshotsDir, "snapshot_20250103_000000.zip");
        await File.WriteAllBytesAsync(snap1, [0x50, 0x4B, 0x05, 0x06]); // minimal zip
        await File.WriteAllBytesAsync(snap2, [0x50, 0x4B, 0x05, 0x06]);
        await File.WriteAllBytesAsync(snap3, [0x50, 0x4B, 0x05, 0x06]);

        svc.EnforceRetention(snapshotsDir);

        var remaining = Directory.GetFiles(snapshotsDir, "*.zip");
        remaining.Should().HaveCount(2);
        File.Exists(snap1).Should().BeFalse("oldest snapshot should be deleted");
        File.Exists(snap3).Should().BeTrue("newest snapshot should be kept");
    }

    [Test]
    public async Task TryRestoreAsync_RestoresFileFromSnapshot()
    {
        await WriteEntityFileAsync("Agent", "abc.json", """{"id":"abc","name":"original"}""");

        var svc = CreateService(MakeOptions());
        await svc.CreateSnapshotAsync(CancellationToken.None);

        // Corrupt the file.
        var filePath = Path.Combine(_dataDir, "Agent", "abc.json");
        await File.WriteAllTextAsync(filePath, "CORRUPT");

        // Restore from snapshot.
        var restored = await svc.TryRestoreAsync(filePath, _dataDir, CancellationToken.None);
        restored.Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("original");
    }

    [Test]
    public async Task TryRestoreAsync_ReturnsFalseWhenNoSnapshot()
    {
        var filePath = Path.Combine(_dataDir, "Agent", "abc.json");
        var svc = CreateService(MakeOptions());

        var restored = await svc.TryRestoreAsync(filePath, _dataDir, CancellationToken.None);
        restored.Should().BeFalse();
    }

    [Test]
    public async Task TryRestoreAsync_ReturnsFalseWhenFileNotInSnapshot()
    {
        await WriteEntityFileAsync("Agent", "abc.json", "{}");
        var svc = CreateService(MakeOptions());
        await svc.CreateSnapshotAsync(CancellationToken.None);

        // Try to restore a file that was never in the snapshot.
        var missingPath = Path.Combine(_dataDir, "Agent", "xyz.json");
        var restored = await svc.TryRestoreAsync(missingPath, _dataDir, CancellationToken.None);
        restored.Should().BeFalse();
    }

    [Test]
    public async Task GetLatestSnapshotPath_ReturnsNewest()
    {
        await WriteEntityFileAsync("Agent", "a.json", "{}");
        var svc = CreateService(MakeOptions());
        await svc.CreateSnapshotAsync(CancellationToken.None);
        await Task.Delay(50);
        var latest = await svc.CreateSnapshotAsync(CancellationToken.None);

        svc.GetLatestSnapshotPath().Should().Be(latest);
    }

    [Test]
    public void GetLatestSnapshotPath_NoSnapshots_ReturnsNull()
    {
        var svc = CreateService(MakeOptions());
        svc.GetLatestSnapshotPath().Should().BeNull();
    }
}
