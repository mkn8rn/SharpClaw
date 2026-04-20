using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Per-directory SHA-256 checksum manifest with HMAC-SHA256 signature (RGAP-6).
/// <para>
/// <b>Phase J deliverables:</b>
/// <list type="bullet">
///   <item>SHA-256 computed on write, stored in <c>_checksums.json</c> per entity directory.</item>
///   <item>HMAC-SHA256 of <c>_checksums.json</c> stored in <c>_checksums.sig</c>,
///         keyed with the encryption key. Prevents simultaneous file + checksum tampering.</item>
///   <item>Optional read-time verification (<see cref="JsonFileOptions.VerifyChecksumsOnRead"/>).</item>
///   <item>Startup full-scan verification during index rebuild.</item>
///   <item>Checksum mismatch → quarantine (Phase F integration).</item>
/// </list>
/// </para>
/// </summary>
internal static class ChecksumManifest
{
    internal const string ManifestFileName = "_checksums.json";
    internal const string SignatureFileName = "_checksums.sig";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Updates the checksum for a single file in the manifest. If the file was
    /// deleted, the entry is removed. After mutation the manifest is atomically
    /// written and the HMAC signature is refreshed.
    /// </summary>
    internal static async Task UpdateChecksumAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        string fileName,
        ReadOnlyMemory<byte> fileBytes,
        bool deleted,
        byte[] hmacKey,
        bool fsync,
        ILogger logger,
        CancellationToken ct)
    {
        var entries = await LoadManifestAsync(fs, entityDir, ct);

        if (deleted)
        {
            entries.Remove(fileName);
        }
        else
        {
            var hash = ComputeSha256(fileBytes.Span);
            entries[fileName] = hash;
        }

        await SaveManifestAsync(fs, entityDir, entries, hmacKey, fsync, ct);
    }

    /// <summary>
    /// Replaces multiple entries in a single manifest write. Used after two-phase commit
    /// to batch-update all checksums that changed in one flush cycle.
    /// </summary>
    internal static async Task UpdateChecksumsAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        IReadOnlyList<(string FileName, ReadOnlyMemory<byte> Data, bool Deleted)> changes,
        byte[] hmacKey,
        bool fsync,
        ILogger logger,
        CancellationToken ct)
    {
        if (changes.Count == 0)
            return;

        var entries = await LoadManifestAsync(fs, entityDir, ct);

        foreach (var (fileName, data, deleted) in changes)
        {
            if (deleted)
            {
                entries.Remove(fileName);
            }
            else
            {
                entries[fileName] = ComputeSha256(data.Span);
            }
        }

        await SaveManifestAsync(fs, entityDir, entries, hmacKey, fsync, ct);
    }

    /// <summary>
    /// Verifies a single file's checksum against the manifest.
    /// Returns <c>true</c> if the checksum matches or if checksums are not available.
    /// Returns <c>false</c> if there is a mismatch (silent corruption detected).
    /// </summary>
    internal static async Task<bool> VerifyFileAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        string fileName,
        ReadOnlyMemory<byte> fileBytes,
        byte[] hmacKey,
        ILogger logger,
        CancellationToken ct)
    {
        var manifestPath = fs.CombinePath(entityDir, ManifestFileName);
        if (!fs.FileExists(manifestPath))
            return true; // No manifest yet — nothing to verify against.

        if (!await VerifySignatureAsync(fs, entityDir, hmacKey, logger, ct))
        {
            logger.LogWarning("HMAC signature mismatch for manifest in {Dir} — manifest may be tampered", entityDir);
            return false;
        }

        var entries = await LoadManifestAsync(fs, entityDir, ct);
        if (!entries.TryGetValue(fileName, out var expectedHash))
            return true; // File not in manifest — new file or pre-checksum era.

        var actualHash = ComputeSha256(fileBytes.Span);
        if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            return true;

        logger.LogWarning(
            "Checksum mismatch for {File} in {Dir}: expected {Expected}, got {Actual}",
            fileName, entityDir, expectedHash, actualHash);
        return false;
    }

    /// <summary>
    /// Full-scan verification of all entity files in a directory against the manifest.
    /// Returns the list of files that have checksum mismatches.
    /// </summary>
    internal static async Task<List<string>> VerifyAllAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        byte[] hmacKey,
        ILogger logger,
        CancellationToken ct)
    {
        var mismatched = new List<string>();

        var manifestPath = fs.CombinePath(entityDir, ManifestFileName);
        if (!fs.FileExists(manifestPath))
            return mismatched;

        if (!await VerifySignatureAsync(fs, entityDir, hmacKey, logger, ct))
        {
            logger.LogWarning(
                "HMAC signature invalid for {Dir} — triggering full rebuild of manifest", entityDir);
            // Can't trust any entries — report all entity files as mismatched
            // so caller can rebuild.
            var allFiles = fs.GetFiles(entityDir, "*.json")
                .Where(f => !IsManifestFile(fs.GetFileName(f)))
                .ToList();
            mismatched.AddRange(allFiles);
            return mismatched;
        }

        var entries = await LoadManifestAsync(fs, entityDir, ct);

        foreach (var (fileName, expectedHash) in entries)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = fs.CombinePath(entityDir, fileName);
            if (!fs.FileExists(filePath))
            {
                logger.LogWarning("Checksum manifest references missing file {File} in {Dir}", fileName, entityDir);
                mismatched.Add(filePath);
                continue;
            }

            try
            {
                using var owned = await fs.ReadAllBytesAsync(filePath, ct);
                var actualHash = ComputeSha256(owned.Span);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Checksum mismatch for {File} in {Dir}: expected {Expected}, got {Actual}",
                        fileName, entityDir, expectedHash, actualHash);
                    mismatched.Add(filePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read {File} during checksum verification", filePath);
                mismatched.Add(filePath);
            }
        }

        return mismatched;
    }

    /// <summary>
    /// Rebuilds the checksum manifest from scratch by hashing every entity file
    /// in the directory. Used after detecting a tampered HMAC signature.
    /// </summary>
    internal static async Task RebuildManifestAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        byte[] hmacKey,
        bool fsync,
        ILogger logger,
        CancellationToken ct)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = fs.GetFiles(entityDir, "*.json")
            .Where(f => !IsManifestFile(fs.GetFileName(f)))
            .ToArray();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var owned = await fs.ReadAllBytesAsync(filePath, ct);
                var fileName = fs.GetFileName(filePath);
                entries[fileName] = ComputeSha256(owned.Span);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping {File} during manifest rebuild", filePath);
            }
        }

        await SaveManifestAsync(fs, entityDir, entries, hmacKey, fsync, ct);
        logger.LogInformation("Rebuilt checksum manifest for {Dir} with {Count} entries", entityDir, entries.Count);
    }

    // ── Private helpers ──────────────────────────────────────────

    private static string ComputeSha256(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, hash);
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] ComputeHmac(ReadOnlySpan<byte> data, byte[] key)
    {
        Span<byte> hmac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(key, data, hmac);
        return hmac.ToArray();
    }

    private static async Task<Dictionary<string, string>> LoadManifestAsync(
        IPersistenceFileSystem fs, string entityDir, CancellationToken ct)
    {
        var path = fs.CombinePath(entityDir, ManifestFileName);
        if (!fs.FileExists(path))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var owned = await fs.ReadAllBytesAsync(path, ct);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(owned.Span, ManifestJsonOptions);
            return dict is not null
                ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task SaveManifestAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        Dictionary<string, string> entries,
        byte[] hmacKey,
        bool fsync,
        CancellationToken ct)
    {
        // Write manifest atomically.
        var json = JsonSerializer.Serialize(entries, ManifestJsonOptions);
        var manifestPath = fs.CombinePath(entityDir, ManifestFileName);
        await AtomicFileWriter.WriteTextAsync(fs, manifestPath, json, fsync, ct);

        // Compute and write HMAC signature (RGAP-6).
        var manifestBytes = Encoding.UTF8.GetBytes(json);
        var hmac = ComputeHmac(manifestBytes, hmacKey);
        var sigPath = fs.CombinePath(entityDir, SignatureFileName);
        var sigHex = Convert.ToHexStringLower(hmac);
        await AtomicFileWriter.WriteTextAsync(fs, sigPath, sigHex, fsync, ct);
    }

    private static async Task<bool> VerifySignatureAsync(
        IPersistenceFileSystem fs,
        string entityDir,
        byte[] hmacKey,
        ILogger logger,
        CancellationToken ct)
    {
        var manifestPath = fs.CombinePath(entityDir, ManifestFileName);
        var sigPath = fs.CombinePath(entityDir, SignatureFileName);

        if (!fs.FileExists(sigPath))
            return true; // No sig file — pre-Phase-J data, trust manifest.

        try
        {
            using var manifestOwned = await fs.ReadAllBytesAsync(manifestPath, ct);
            var expectedHmac = ComputeHmac(manifestOwned.Span, hmacKey);
            var expectedHex = Convert.ToHexStringLower(expectedHmac);

            using var sigOwned = await fs.ReadAllBytesAsync(sigPath, ct);
            var actualHex = Encoding.UTF8.GetString(sigOwned.Span).Trim();

            return string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify HMAC signature for {Dir}", entityDir);
            return false;
        }
    }

    /// <summary>
    /// Returns true for manifest/signature files that should be excluded from
    /// entity file enumeration.
    /// </summary>
    internal static bool IsManifestFile(string fileName) =>
        string.Equals(fileName, ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, SignatureFileName, StringComparison.OrdinalIgnoreCase);
}
