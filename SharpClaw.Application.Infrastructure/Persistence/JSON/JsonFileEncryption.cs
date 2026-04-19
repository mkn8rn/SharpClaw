using System.Text;
using SharpClaw.Utils.Security;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Shared read/write helpers for encrypted JSON file persistence.
/// Used by both <see cref="JsonFilePersistenceService"/> and future cold-entity stores.
/// </summary>
internal static class JsonFileEncryption
{
    /// <summary>
    /// Reads a file from disk, auto-detecting encrypted vs plaintext.
    /// First byte 0x01 means encrypted envelope; otherwise treat as UTF-8 JSON.
    /// This allows transparent migration from unencrypted data.
    /// </summary>
    public static async Task<string> ReadJsonAsync(string path, byte[] key, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length >= ApiKeyEncryptor.MinEnvelopeSize && bytes[0] == 0x01)
        {
            var plain = ApiKeyEncryptor.DecryptBytes(bytes, key);
            return Encoding.UTF8.GetString(plain);
        }

        // Legacy plaintext, encryption disabled, or BOM-prefixed file.
        // UTF-8 BOM (0xEF 0xBB 0xBF) is not 0x01, so falls through correctly.
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Writes a JSON string to disk, encrypting if configured.
    /// </summary>
    public static async Task WriteJsonAsync(string path, string json, byte[] key, bool encrypt, CancellationToken ct)
    {
        if (encrypt)
        {
            var plain = Encoding.UTF8.GetBytes(json);
            var encrypted = ApiKeyEncryptor.EncryptBytes(plain, key);
            await File.WriteAllBytesAsync(path, encrypted, ct);
        }
        else
        {
            await File.WriteAllTextAsync(path, json, ct);
        }
    }

    /// <summary>
    /// Writes pre-serialized UTF-8 bytes to disk, encrypting if configured.
    /// Avoids the string intermediary when the caller already has raw bytes.
    /// </summary>
    public static async Task WriteBytesAsync(string path, byte[] utf8Json, byte[] key, bool encrypt, CancellationToken ct)
    {
        if (encrypt)
        {
            var encrypted = ApiKeyEncryptor.EncryptBytes(utf8Json, key);
            await File.WriteAllBytesAsync(path, encrypted, ct);
        }
        else
        {
            await File.WriteAllBytesAsync(path, utf8Json, ct);
        }
    }
}
