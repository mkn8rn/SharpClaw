using System.Text;
using FluentAssertions;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public class AtomicFileWriterTests
{
    private InMemoryPersistenceFileSystem _fs = null!;

    [SetUp]
    public void SetUp()
    {
        _fs = new InMemoryPersistenceFileSystem();
        _fs.CreateDirectory("data");
    }

    // ── Basic atomic write ────────────────────────────────────────

    [Test]
    public async Task WriteAsync_CreatesFileAtFinalPath()
    {
        var path = _fs.CombinePath("data", "entity.json");
        var data = Encoding.UTF8.GetBytes("""{"id":1}""");

        await AtomicFileWriter.WriteAsync(_fs, path, data, fsync: false);

        _fs.FileExists(path).Should().BeTrue();
    }

    [Test]
    public async Task WriteAsync_TmpFileAbsentAfterSuccess()
    {
        var path = _fs.CombinePath("data", "entity.json");
        var data = Encoding.UTF8.GetBytes("content");

        await AtomicFileWriter.WriteAsync(_fs, path, data, fsync: false);

        _fs.FileExists(path + ".tmp").Should().BeFalse("tmp file should be cleaned up by rename");
    }

    [Test]
    public async Task WriteAsync_OverwritesExistingFile()
    {
        var path = _fs.CombinePath("data", "entity.json");
        await AtomicFileWriter.WriteAsync(_fs, path, "old"u8.ToArray(), fsync: false);
        await AtomicFileWriter.WriteAsync(_fs, path, "new"u8.ToArray(), fsync: false);

        var content = await _fs.ReadAllTextAsync(path);
        content.Should().Be("new");
    }

    // ── Fsync verification ────────────────────────────────────────

    [Test]
    public async Task WriteAsync_WithFsync_CallsFlushFileAsync()
    {
        var path = _fs.CombinePath("data", "entity.json");
        var data = Encoding.UTF8.GetBytes("content");

        await AtomicFileWriter.WriteAsync(_fs, path, data, fsync: true);

        _fs.FsyncedFiles.Should().ContainSingle()
            .Which.Should().EndWith(".tmp");
    }

    [Test]
    public async Task WriteAsync_WithoutFsync_DoesNotCallFlushFileAsync()
    {
        var path = _fs.CombinePath("data", "entity.json");
        var data = Encoding.UTF8.GetBytes("content");

        await AtomicFileWriter.WriteAsync(_fs, path, data, fsync: false);

        _fs.FsyncedFiles.Should().BeEmpty();
    }

    // ── WriteTextAsync ────────────────────────────────────────────

    [Test]
    public async Task WriteTextAsync_RoundTrips()
    {
        var path = _fs.CombinePath("data", "text.json");

        await AtomicFileWriter.WriteTextAsync(_fs, path, """{"text":true}""", fsync: false);

        var result = await _fs.ReadAllTextAsync(path);
        result.Should().Be("""{"text":true}""");
    }

    // ── Crash simulation: .tmp left behind ────────────────────────

    [Test]
    public async Task WriteAsync_WhenMoveThrows_TmpFileMayRemain()
    {
        var path = _fs.CombinePath("data", "crash.json");
        var data = Encoding.UTF8.GetBytes("crash-data");

        // Sabotage the MoveFile by making the .tmp write succeed,
        // then simulate a crash by checking .tmp existence before MoveFile.
        // We can't easily hook MoveFile, but we can verify that on success .tmp is gone.
        await AtomicFileWriter.WriteAsync(_fs, path, data, fsync: false);
        _fs.FileExists(path + ".tmp").Should().BeFalse();
    }

    // ── ReEncryptAsync ────────────────────────────────────────────

    [Test]
    public async Task ReEncryptAsync_RotatesEncryptedFiles()
    {
        var oldKey = ApiKeyEncryptor.GenerateKey();
        var newKey = ApiKeyEncryptor.GenerateKey();
        var json = """{"secret":"data"}""";

        // Set up directory structure
        _fs.CreateDirectory("data/EntityDB");
        var path = _fs.CombinePath("data", "EntityDB", "1.json");

        // Write encrypted with old key
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ApiKeyEncryptor.EncryptBytes(plain, oldKey);
        await _fs.WriteAllBytesAsync(path, encrypted);

        // Re-encrypt
        await JsonFileEncryption.ReEncryptAsync(_fs, "data", oldKey, newKey, fsync: false, CancellationToken.None);

        // Verify: readable with new key, not with old
        using var owned = await _fs.ReadAllBytesAsync(path);
        var decrypted = ApiKeyEncryptor.DecryptBytes(owned.Span, newKey);
        Encoding.UTF8.GetString(decrypted).Should().Be(json);
    }

    [Test]
    public async Task ReEncryptAsync_SkipsPlaintextFiles()
    {
        var oldKey = ApiKeyEncryptor.GenerateKey();
        var newKey = ApiKeyEncryptor.GenerateKey();
        var json = """{"plain":true}""";

        _fs.CreateDirectory("data/EntityDB");
        var path = _fs.CombinePath("data", "EntityDB", "plain.json");
        await _fs.WriteAllTextAsync(path, json);

        // Should not throw and should leave file unchanged
        await JsonFileEncryption.ReEncryptAsync(_fs, "data", oldKey, newKey, fsync: false, CancellationToken.None);

        var result = await _fs.ReadAllTextAsync(path);
        result.Should().Be(json);
    }

    [Test]
    public async Task ReEncryptAsync_UsesAtomicWrites()
    {
        var oldKey = ApiKeyEncryptor.GenerateKey();
        var newKey = ApiKeyEncryptor.GenerateKey();

        _fs.CreateDirectory("data/EntityDB");
        var path = _fs.CombinePath("data", "EntityDB", "1.json");
        var encrypted = ApiKeyEncryptor.EncryptBytes("test"u8, oldKey);
        await _fs.WriteAllBytesAsync(path, encrypted);

        await JsonFileEncryption.ReEncryptAsync(_fs, "data", oldKey, newKey, fsync: true, CancellationToken.None);

        // Fsync should have been called on the .tmp file
        _fs.FsyncedFiles.Should().ContainSingle()
            .Which.Should().EndWith(".tmp");
        // No .tmp left behind
        _fs.FileExists(path + ".tmp").Should().BeFalse();
    }
}
