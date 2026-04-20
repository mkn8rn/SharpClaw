using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

/// <summary>
/// Phase J tests: Per-file SHA-256 checksum manifest with HMAC-SHA256 signature.
/// </summary>
[TestFixture]
public class ChecksumManifestTests
{
    private string _dataDir = null!;
    private string _entityDir = null!;
    private byte[] _key = null!;
    private IPersistenceFileSystem _fs = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"checksum_{Guid.NewGuid():N}");
        _entityDir = Path.Combine(_dataDir, "TestEntity");
        Directory.CreateDirectory(_entityDir);
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

    private async Task WriteFileAsync(string fileName, string content)
    {
        var path = Path.Combine(_entityDir, fileName);
        await File.WriteAllTextAsync(path, content);
    }

    private async Task UpdateChecksumForFileAsync(string fileName, byte[]? data = null)
    {
        data ??= await File.ReadAllBytesAsync(Path.Combine(_entityDir, fileName));
        await ChecksumManifest.UpdateChecksumAsync(
            _fs, _entityDir, fileName, data, deleted: false, _key, fsync: false, _logger, CancellationToken.None);
    }

    // ── Write updates manifest ───────────────────────────────────

    [Test]
    public async Task WhenFileWrittenThenManifestContainsChecksum()
    {
        await WriteFileAsync("a.json", """{"id":"1"}""");
        await UpdateChecksumForFileAsync("a.json");

        var manifestPath = Path.Combine(_entityDir, ChecksumManifest.ManifestFileName);
        var sigPath = Path.Combine(_entityDir, ChecksumManifest.SignatureFileName);

        manifestPath.Should().Match(p => File.Exists((string)p));
        sigPath.Should().Match(p => File.Exists((string)p));

        var json = await File.ReadAllTextAsync(manifestPath);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        dict.Should().ContainKey("a.json");
        dict!["a.json"].Should().HaveLength(64); // SHA-256 hex
    }

    [Test]
    public async Task WhenFileDeletedThenEntryRemovedFromManifest()
    {
        await WriteFileAsync("b.json", """{"id":"2"}""");
        await UpdateChecksumForFileAsync("b.json");

        await ChecksumManifest.UpdateChecksumAsync(
            _fs, _entityDir, "b.json", ReadOnlyMemory<byte>.Empty,
            deleted: true, _key, fsync: false, _logger, CancellationToken.None);

        var json = await File.ReadAllTextAsync(
            Path.Combine(_entityDir, ChecksumManifest.ManifestFileName));
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        dict.Should().NotContainKey("b.json");
    }

    // ── Tampered file detection ──────────────────────────────────

    [Test]
    public async Task WhenFileTamperedThenVerifyFileReturnsFalse()
    {
        var content = """{"id":"3"}""";
        await WriteFileAsync("c.json", content);
        await UpdateChecksumForFileAsync("c.json");

        // Tamper with the file.
        await File.WriteAllTextAsync(Path.Combine(_entityDir, "c.json"), """{"id":"HACKED"}""");
        var tamperedBytes = await File.ReadAllBytesAsync(Path.Combine(_entityDir, "c.json"));

        var valid = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "c.json", tamperedBytes, _key, _logger, CancellationToken.None);

        valid.Should().BeFalse();
    }

    [Test]
    public async Task WhenFileIntactThenVerifyFileReturnsTrue()
    {
        var content = """{"id":"4"}""";
        await WriteFileAsync("d.json", content);
        await UpdateChecksumForFileAsync("d.json");

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_entityDir, "d.json"));
        var valid = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "d.json", bytes, _key, _logger, CancellationToken.None);

        valid.Should().BeTrue();
    }

    // ── HMAC tampering detection ─────────────────────────────────

    [Test]
    public async Task WhenHmacSignatureTamperedThenVerifyFileReturnsFalse()
    {
        await WriteFileAsync("e.json", """{"id":"5"}""");
        await UpdateChecksumForFileAsync("e.json");

        // Tamper with the signature.
        var sigPath = Path.Combine(_entityDir, ChecksumManifest.SignatureFileName);
        await File.WriteAllTextAsync(sigPath, "0000000000000000000000000000000000000000000000000000000000000000");

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_entityDir, "e.json"));
        var valid = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "e.json", bytes, _key, _logger, CancellationToken.None);

        valid.Should().BeFalse();
    }

    // ── Full-scan verification ───────────────────────────────────

    [Test]
    public async Task WhenFullScanThenOnlyTamperedFilesReturned()
    {
        await WriteFileAsync("ok.json", """{"ok":true}""");
        await WriteFileAsync("bad.json", """{"bad":false}""");
        await UpdateChecksumForFileAsync("ok.json");
        await UpdateChecksumForFileAsync("bad.json");

        // Tamper one file.
        await File.WriteAllTextAsync(Path.Combine(_entityDir, "bad.json"), "CORRUPTED");

        var mismatched = await ChecksumManifest.VerifyAllAsync(
            _fs, _entityDir, _key, _logger, CancellationToken.None);

        mismatched.Should().HaveCount(1);
        mismatched[0].Should().Contain("bad.json");
    }

    [Test]
    public async Task WhenHmacTamperedThenFullScanReturnsAllFiles()
    {
        await WriteFileAsync("f1.json", """{"f":1}""");
        await WriteFileAsync("f2.json", """{"f":2}""");
        await UpdateChecksumForFileAsync("f1.json");
        await UpdateChecksumForFileAsync("f2.json");

        var sigPath = Path.Combine(_entityDir, ChecksumManifest.SignatureFileName);
        await File.WriteAllTextAsync(sigPath, "bad_sig");

        var mismatched = await ChecksumManifest.VerifyAllAsync(
            _fs, _entityDir, _key, _logger, CancellationToken.None);

        // HMAC invalid → all entity .json files are flagged.
        mismatched.Should().HaveCount(2);
    }

    // ── Manifest rebuild ─────────────────────────────────────────

    [Test]
    public async Task WhenManifestRebuiltThenAllEntityFilesIncluded()
    {
        await WriteFileAsync("x.json", """{"x":1}""");
        await WriteFileAsync("y.json", """{"y":2}""");

        await ChecksumManifest.RebuildManifestAsync(
            _fs, _entityDir, _key, fsync: false, _logger, CancellationToken.None);

        // Verify all files pass after rebuild.
        var mismatched = await ChecksumManifest.VerifyAllAsync(
            _fs, _entityDir, _key, _logger, CancellationToken.None);

        mismatched.Should().BeEmpty();
    }

    // ── Batch update ─────────────────────────────────────────────

    [Test]
    public async Task WhenBatchUpdateThenAllEntriesWritten()
    {
        await WriteFileAsync("b1.json", """{"b":1}""");
        await WriteFileAsync("b2.json", """{"b":2}""");

        var b1 = await File.ReadAllBytesAsync(Path.Combine(_entityDir, "b1.json"));
        var b2 = await File.ReadAllBytesAsync(Path.Combine(_entityDir, "b2.json"));

        var changes = new List<(string FileName, ReadOnlyMemory<byte> Data, bool Deleted)>
        {
            ("b1.json", b1, false),
            ("b2.json", b2, false),
        };

        await ChecksumManifest.UpdateChecksumsAsync(
            _fs, _entityDir, changes, _key, fsync: false, _logger, CancellationToken.None);

        var valid1 = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "b1.json", b1, _key, _logger, CancellationToken.None);
        var valid2 = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "b2.json", b2, _key, _logger, CancellationToken.None);

        valid1.Should().BeTrue();
        valid2.Should().BeTrue();
    }

    // ── No manifest = passes ─────────────────────────────────────

    [Test]
    public async Task WhenNoManifestExistsThenVerifyReturnsTrue()
    {
        var bytes = Encoding.UTF8.GetBytes("anything");
        var valid = await ChecksumManifest.VerifyFileAsync(
            _fs, _entityDir, "noexist.json", bytes, _key, _logger, CancellationToken.None);

        valid.Should().BeTrue();
    }

    [Test]
    public async Task WhenNoManifestExistsThenFullScanReturnsEmpty()
    {
        await WriteFileAsync("z.json", "data");

        var mismatched = await ChecksumManifest.VerifyAllAsync(
            _fs, _entityDir, _key, _logger, CancellationToken.None);

        mismatched.Should().BeEmpty();
    }
}
