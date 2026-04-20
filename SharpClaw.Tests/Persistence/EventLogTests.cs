using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

/// <summary>
/// Phase H tests: Append-only JSONL event log with daily rotation, retention purge,
/// and per-line encryption (RGAP-7).
/// </summary>
[TestFixture]
public class EventLogTests
{
    private string _dataDir = null!;
    private byte[] _key = null!;
    private IPersistenceFileSystem _fs = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"eventlog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDir);
        _key = ApiKeyEncryptor.GenerateKey();
        _fs = new PhysicalPersistenceFileSystem();
        _logger = NullLogger.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private JsonFileOptions MakeOptions(bool enableLog = true, bool encrypt = false, int retentionDays = 7)
        => new()
        {
            DataDirectory = _dataDir,
            EnableEventLog = enableLog,
            EncryptAtRest = encrypt,
            EventLogRetentionDays = retentionDays,
        };

    private EventLog CreateLog(JsonFileOptions options, byte[]? key = null)
        => new(_fs, options, key, _logger);

    private static IReadOnlyList<(Type ClrType, Guid Id, EntityState State)> SampleChanges(int count = 1)
    {
        var list = new List<(Type, Guid, EntityState)>();
        for (var i = 0; i < count; i++)
            list.Add((typeof(FakeEntity), Guid.NewGuid(), EntityState.Added));
        return list;
    }

    // Dummy type for EntityType name.
    private sealed class FakeEntity;

    // ── Tests ────────────────────────────────────────────────────

    [Test]
    public async Task AppendAndRead_Plaintext_RoundTrips()
    {
        var options = MakeOptions();
        var log = CreateLog(options);
        var id = Guid.NewGuid();
        var changes = new List<(Type, Guid, EntityState)> { (typeof(FakeEntity), id, EntityState.Added) };

        await log.AppendAsync(changes, CancellationToken.None);

        var entries = await log.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        entries.Should().HaveCount(1);
        entries[0].EntityType.Should().Be("FakeEntity");
        entries[0].EntityId.Should().Be(id);
        entries[0].Action.Should().Be(EventAction.Created);
    }

    [Test]
    public async Task AppendAndRead_Encrypted_RoundTrips()
    {
        var options = MakeOptions(encrypt: true);
        var log = CreateLog(options, _key);
        var changes = SampleChanges(3);

        await log.AppendAsync(changes, CancellationToken.None);

        var entries = await log.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        entries.Should().HaveCount(3);
        entries.Should().OnlyContain(e => e.Action == EventAction.Created);
    }

    [Test]
    public async Task ReadAsync_EmptyLog_ReturnsEmptyList()
    {
        var log = CreateLog(MakeOptions());

        var entries = await log.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        entries.Should().BeEmpty();
    }

    [Test]
    public async Task ReadAllAsync_NoEventsDirectory_ReturnsEmptyList()
    {
        var log = CreateLog(MakeOptions());

        var entries = await log.ReadAllAsync(CancellationToken.None);

        entries.Should().BeEmpty();
    }

    [Test]
    public async Task DailyRotation_SeparateFiles_PerDate()
    {
        var options = MakeOptions();
        var eventsDir = Path.Combine(_dataDir, "_events");
        Directory.CreateDirectory(eventsDir);

        // Simulate two days by writing files directly.
        var day1 = "20250601";
        var day2 = "20250602";
        var entry1 = """{"timestamp":"2025-06-01T00:00:00+00:00","entityType":"A","entityId":"00000000-0000-0000-0000-000000000001","action":"Created"}""";
        var entry2 = """{"timestamp":"2025-06-02T00:00:00+00:00","entityType":"B","entityId":"00000000-0000-0000-0000-000000000002","action":"Deleted"}""";
        await File.WriteAllTextAsync(Path.Combine(eventsDir, $"events_{day1}.jsonl"), entry1 + "\n");
        await File.WriteAllTextAsync(Path.Combine(eventsDir, $"events_{day2}.jsonl"), entry2 + "\n");

        var log = CreateLog(options);
        var all = await log.ReadAllAsync(CancellationToken.None);

        all.Should().HaveCount(2);
        all[0].EntityType.Should().Be("A");
        all[1].EntityType.Should().Be("B");
    }

    [Test]
    public async Task PurgeExpiredLogs_DeletesOldFiles_KeepsRecent()
    {
        var options = MakeOptions(retentionDays: 2);
        var eventsDir = Path.Combine(_dataDir, "_events");
        Directory.CreateDirectory(eventsDir);

        // Old file (10 days ago).
        var old = DateTimeOffset.UtcNow.AddDays(-10).UtcDateTime.ToString("yyyyMMdd");
        var today = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMdd");
        await File.WriteAllTextAsync(Path.Combine(eventsDir, $"events_{old}.jsonl"), "x\n");
        await File.WriteAllTextAsync(Path.Combine(eventsDir, $"events_{today}.jsonl"), "x\n");

        var log = CreateLog(options);
        log.PurgeExpiredLogs();

        File.Exists(Path.Combine(eventsDir, $"events_{old}.jsonl")).Should().BeFalse();
        File.Exists(Path.Combine(eventsDir, $"events_{today}.jsonl")).Should().BeTrue();
    }

    [Test]
    public async Task MixedEncryptedAndPlaintext_ReadSucceeds()
    {
        var options = MakeOptions(encrypt: true);
        var eventsDir = Path.Combine(_dataDir, "_events");
        Directory.CreateDirectory(eventsDir);

        var plainJson = """{"timestamp":"2025-06-01T00:00:00+00:00","entityType":"Plain","entityId":"00000000-0000-0000-0000-000000000001","action":"Updated"}""";
        var encJson = """{"timestamp":"2025-06-01T00:00:00+00:00","entityType":"Enc","entityId":"00000000-0000-0000-0000-000000000002","action":"Deleted"}""";
        var encrypted = Convert.ToBase64String(ApiKeyEncryptor.EncryptBytes(Encoding.UTF8.GetBytes(encJson), _key));

        var today = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyyMMdd");
        await File.WriteAllTextAsync(
            Path.Combine(eventsDir, $"events_{today}.jsonl"),
            plainJson + "\n" + encrypted + "\n");

        var log = CreateLog(options, _key);
        var entries = await log.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.EntityType == "Plain");
        entries.Should().Contain(e => e.EntityType == "Enc");
    }

    [Test]
    public async Task AppendAsync_DisabledEventLog_WritesNothing()
    {
        var options = MakeOptions(enableLog: false);
        var log = CreateLog(options);

        await log.AppendAsync(SampleChanges(5), CancellationToken.None);

        var eventsDir = Path.Combine(_dataDir, "_events");
        Directory.Exists(eventsDir).Should().BeFalse();
    }

    [Test]
    public async Task AppendAsync_MapsEntityStatesCorrectly()
    {
        var options = MakeOptions();
        var log = CreateLog(options);
        var changes = new List<(Type, Guid, EntityState)>
        {
            (typeof(FakeEntity), Guid.NewGuid(), EntityState.Added),
            (typeof(FakeEntity), Guid.NewGuid(), EntityState.Modified),
            (typeof(FakeEntity), Guid.NewGuid(), EntityState.Deleted),
        };

        await log.AppendAsync(changes, CancellationToken.None);

        var entries = await log.ReadAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        entries.Should().HaveCount(3);
        entries[0].Action.Should().Be(EventAction.Created);
        entries[1].Action.Should().Be(EventAction.Updated);
        entries[2].Action.Should().Be(EventAction.Deleted);
    }
}
