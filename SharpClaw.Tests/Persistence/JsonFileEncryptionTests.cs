using System.Text;
using SharpClaw.Infrastructure.Persistence.JSON;
using SharpClaw.Utils.Security;

namespace SharpClaw.Tests.Persistence;

[TestFixture]
public class JsonFileEncryptionTests
{
    private string _tempDir = null!;
    private byte[] _key = null!;
    private IPersistenceFileSystem _fs = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jfe_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _key = ApiKeyEncryptor.GenerateKey();
        _fs = new PhysicalPersistenceFileSystem();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── WriteJsonAsync / ReadJsonAsync round-trip ──────────────────

    [Test]
    public async Task WriteJsonAsync_Encrypted_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "enc.json");
        var json = """{"Name":"test","Value":42}""";

        await JsonFileEncryption.WriteJsonAsync(_fs, path, json, _key, encrypt: true, fsync: false, CancellationToken.None);
        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Be(json);
    }

    [Test]
    public async Task WriteJsonAsync_Plaintext_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "plain.json");
        var json = """{"Name":"test"}""";

        await JsonFileEncryption.WriteJsonAsync(_fs, path, json, _key, encrypt: false, fsync: false, CancellationToken.None);
        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Be(json);
    }

    [Test]
    public async Task WriteJsonAsync_Encrypted_ProducesEnvelopeOnDisk()
    {
        var path = Path.Combine(_tempDir, "env.json");
        await JsonFileEncryption.WriteJsonAsync(_fs, path, "{}", _key, encrypt: true, fsync: false, CancellationToken.None);

        var raw = await File.ReadAllBytesAsync(path);
        raw[0].Should().Be(0x01, "file should start with version byte");
        raw.Length.Should().BeGreaterThanOrEqualTo(ApiKeyEncryptor.MinEnvelopeSize);
    }

    // ── Legacy plaintext auto-detect ──────────────────────────────

    [Test]
    public async Task ReadJsonAsync_AutoDetectsPlaintext()
    {
        var path = Path.Combine(_tempDir, "legacy.json");
        await File.WriteAllTextAsync(path, """{"legacy":true}""");

        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Be("""{"legacy":true}""");
    }

    [Test]
    public async Task ReadJsonAsync_AutoDetectsUtf8BomPlaintext()
    {
        var path = Path.Combine(_tempDir, "bom.json");
        await File.WriteAllTextAsync(path, """{"bom":true}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Contain("bom");
    }

    // ── Key mismatch ──────────────────────────────────────────────

    [Test]
    public async Task ReadJsonAsync_WrongKey_Throws()
    {
        var path = Path.Combine(_tempDir, "mismatch.json");
        await JsonFileEncryption.WriteJsonAsync(_fs, path, """{"secret":1}""", _key, encrypt: true, fsync: false, CancellationToken.None);

        var wrongKey = ApiKeyEncryptor.GenerateKey();

        var act = async () => await JsonFileEncryption.ReadJsonAsync(_fs, path, wrongKey, CancellationToken.None);

        await act.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
    }

    // ── WriteBytesAsync ───────────────────────────────────────────

    [Test]
    public async Task WriteBytesAsync_Encrypted_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "bytes.json");
        var utf8 = Encoding.UTF8.GetBytes("""{"val":"bytes"}""");

        await JsonFileEncryption.WriteBytesAsync(_fs, path, utf8, _key, encrypt: true, fsync: false, CancellationToken.None);
        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Be("""{"val":"bytes"}""");
    }

    [Test]
    public async Task WriteBytesAsync_Plaintext_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "bytes_plain.json");
        var utf8 = Encoding.UTF8.GetBytes("""{"val":"plain"}""");

        await JsonFileEncryption.WriteBytesAsync(_fs, path, utf8, _key, encrypt: false, fsync: false, CancellationToken.None);
        var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);

        result.Should().Be("""{"val":"plain"}""");
    }

    // ── Flag toggle scenario ──────────────────────────────────────

    [Test]
    public async Task WhenEncryptionToggledOff_OldEncryptedFilesStillReadable()
    {
        var path = Path.Combine(_tempDir, "toggle.json");
        var json = """{"toggle":"data"}""";

        // Write encrypted
        await JsonFileEncryption.WriteJsonAsync(_fs, path, json, _key, encrypt: true, fsync: false, CancellationToken.None);

                // Read still works (auto-detect)
                var result = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);
                result.Should().Be(json);

                // Overwrite as plaintext (simulating toggle off)
                await JsonFileEncryption.WriteJsonAsync(_fs, path, json, _key, encrypt: false, fsync: false, CancellationToken.None);

        // Read still works (plaintext path)
        var result2 = await JsonFileEncryption.ReadJsonAsync(_fs, path, _key, CancellationToken.None);
        result2.Should().Be(json);
    }
}
