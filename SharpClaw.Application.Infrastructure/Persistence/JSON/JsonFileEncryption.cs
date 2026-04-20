using System.Text;
using SharpClaw.Utils.Security;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Shared read/write helpers for encrypted JSON file persistence.
/// Used by both <see cref="JsonFilePersistenceService"/> and cold-entity stores.
/// All I/O is delegated to <see cref="IPersistenceFileSystem"/> — no direct
/// <c>File.*</c> calls.
/// </summary>
internal static class JsonFileEncryption
{
    /// <summary>
    /// Reads a file from disk via <paramref name="fs"/>, auto-detecting
    /// encrypted vs plaintext. Returns the raw UTF-8 bytes in a pooled
    /// buffer — callers should deserialize directly from the span and
    /// then dispose the result to return the buffer to the pool.
    /// </summary>
    public static async Task<OwnedMemory> ReadBytesAsync(
        IPersistenceFileSystem fs, string path, byte[] key, CancellationToken ct)
    {
        var owned = await fs.ReadAllBytesAsync(path, ct);
        var span = owned.Span;

        if (span.Length >= ApiKeyEncryptor.MinEnvelopeSize && span[0] == 0x01)
        {
            var plain = ApiKeyEncryptor.DecryptBytes(span, key);
            owned.Dispose();
            return OwnedMemory.WrapUnpooled(plain);
        }

        // Legacy plaintext, encryption disabled, or BOM-prefixed file.
        return owned;
    }

    /// <summary>
    /// Reads a file and returns the content as a UTF-8 string.
    /// Prefer <see cref="ReadBytesAsync"/> + direct span deserialization
    /// to avoid this string allocation on hot paths.
    /// </summary>
    public static async Task<string> ReadJsonAsync(
        IPersistenceFileSystem fs, string path, byte[] key, CancellationToken ct)
    {
        using var owned = await ReadBytesAsync(fs, path, key, ct);
        return Encoding.UTF8.GetString(owned.Span);
    }

    /// <summary>
    /// Writes a JSON string to disk, encrypting if configured.
    /// Uses atomic write (tmp + fsync + rename) for crash safety.
    /// </summary>
    public static async Task WriteJsonAsync(
        IPersistenceFileSystem fs, string path, string json,
        byte[] key, bool encrypt, bool fsync, CancellationToken ct)
    {
        if (encrypt)
        {
            var plain = Encoding.UTF8.GetBytes(json);
            var encrypted = ApiKeyEncryptor.EncryptBytes(plain, key);
            await AtomicFileWriter.WriteAsync(fs, path, encrypted, fsync, ct);
        }
        else
        {
            await AtomicFileWriter.WriteTextAsync(fs, path, json, fsync, ct);
        }
    }

    /// <summary>
    /// Writes pre-serialized UTF-8 bytes to disk, encrypting if configured.
    /// Uses atomic write (tmp + fsync + rename) for crash safety.
    /// </summary>
    public static async Task WriteBytesAsync(
        IPersistenceFileSystem fs, string path, ReadOnlyMemory<byte> utf8Json,
        byte[] key, bool encrypt, bool fsync, CancellationToken ct)
    {
        if (encrypt)
        {
            var encrypted = ApiKeyEncryptor.EncryptBytes(utf8Json.Span, key);
            await AtomicFileWriter.WriteAsync(fs, path, encrypted, fsync, ct);
        }
        else
        {
            await AtomicFileWriter.WriteAsync(fs, path, utf8Json, fsync, ct);
        }
    }

    /// <summary>
    /// Prepares encrypted or plaintext bytes for staging (no file I/O).
    /// Used by <see cref="TwoPhaseCommit"/> to stage data before commit.
    /// </summary>
    public static ReadOnlyMemory<byte> PrepareBytes(
        ReadOnlyMemory<byte> utf8Json, byte[] key, bool encrypt)
    {
        if (encrypt)
            return ApiKeyEncryptor.EncryptBytes(utf8Json.Span, key);
        return utf8Json;
    }

    /// <summary>
    /// Prepares encrypted or plaintext bytes from a JSON string for staging.
    /// </summary>
    public static ReadOnlyMemory<byte> PrepareJson(
        string json, byte[] key, bool encrypt)
    {
        var plain = Encoding.UTF8.GetBytes(json);
        if (encrypt)
            return ApiKeyEncryptor.EncryptBytes(plain, key);
        return plain;
    }

    /// <summary>
    /// RGAP-12: Re-encrypts all encrypted entity files in a directory tree
    /// from <paramref name="oldKey"/> to <paramref name="newKey"/> using
    /// atomic writes. Non-encrypted files are skipped.
    /// </summary>
    public static async Task ReEncryptAsync(
        IPersistenceFileSystem fs, string dataDirectory,
        byte[] oldKey, byte[] newKey, bool fsync, CancellationToken ct)
    {
        if (!fs.DirectoryExists(dataDirectory))
            return;

        var entityDirs = fs.GetDirectories(dataDirectory);
        foreach (var dir in entityDirs)
        {
            var files = fs.GetFiles(dir, "*.json");
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                using var owned = await fs.ReadAllBytesAsync(file, ct);
                var span = owned.Span;

                // Only re-encrypt files with the 0x01 encryption envelope.
                if (span.Length < ApiKeyEncryptor.MinEnvelopeSize || span[0] != 0x01)
                    continue;

                var plain = ApiKeyEncryptor.DecryptBytes(span, oldKey);
                var reEncrypted = ApiKeyEncryptor.EncryptBytes(plain, newKey);
                await AtomicFileWriter.WriteAsync(fs, file, reEncrypted, fsync, ct);
            }
        }
    }
}
