using System.Text;
using System.Text.Json;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Implements two-phase commit for multi-file flush operations.
/// <list type="number">
///   <item><b>Stage 1:</b> Write all data to <c>.tmp</c> files (with optional fsync).</item>
///   <item><b>Stage 1.5 (RGAP-1):</b> Write <c>_commit.marker</c> listing all
///     <c>.tmp → final</c> path pairs. This is the point-of-no-return.</item>
///   <item><b>Stage 2:</b> Rename all <c>.tmp → final</c> in a single pass.</item>
/// </list>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item>Before marker: <b>rollback</b> — delete all <c>.tmp</c> files.</item>
///   <item>After marker: <b>roll forward</b> — replay remaining renames from the marker.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class TwoPhaseCommit(IPersistenceFileSystem fs, bool fsync)
{
    internal const string CommitMarkerFileName = "_commit.marker";

    private readonly List<StagedFile> _staged = [];

    /// <summary>
    /// Stages raw bytes for a file. The data is written to <c>{path}.tmp</c> immediately.
    /// </summary>
    public async Task StageAsync(string finalPath, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var tmpPath = finalPath + ".tmp";
        await fs.WriteAllBytesAsync(tmpPath, data, ct);
        if (fsync)
            await fs.FlushFileAsync(tmpPath, ct);
        _staged.Add(new StagedFile(tmpPath, finalPath));
    }

    /// <summary>
    /// Stages a UTF-8 text string for a file.
    /// </summary>
    public async Task StageTextAsync(string finalPath, string text, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(text);
        await StageAsync(finalPath, data, ct);
    }

    /// <summary>
    /// Stages a deletion. The file is removed during the commit pass.
    /// </summary>
    public void StageDelete(string finalPath)
    {
        _staged.Add(new StagedFile(TmpPath: null, finalPath, IsDelete: true));
    }

    /// <summary>
    /// Commits all staged files atomically:
    /// writes the commit marker, renames all <c>.tmp → final</c>,
    /// executes deletions, then removes the marker.
    /// </summary>
    /// <param name="markerDir">Directory where <c>_commit.marker</c> is written.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task CommitAsync(string markerDir, CancellationToken ct)
    {
        if (_staged.Count == 0)
            return;

        var markerPath = fs.CombinePath(markerDir, CommitMarkerFileName);

        try
        {
            // Stage 1.5 (RGAP-1): Write commit marker — point of no return.
            var markerEntries = _staged.Select(s => new MarkerEntry(s.TmpPath, s.FinalPath, s.IsDelete)).ToList();
            var markerJson = JsonSerializer.Serialize(markerEntries, MarkerJsonOptions);
            await fs.WriteAllTextAsync(markerPath, markerJson, ct);
            if (fsync)
                await fs.FlushFileAsync(markerPath, ct);

            // Stage 2: Execute renames and deletes.
            ExecuteFromMarker(_staged, fs);

            // Done — remove the marker.
            if (fs.FileExists(markerPath))
                fs.DeleteFile(markerPath);
        }
        catch
        {
            // If the marker file does NOT exist yet, we can rollback (delete .tmp files).
            if (!fs.FileExists(markerPath))
            {
                Rollback();
                throw;
            }

            // Marker exists → we must roll forward (caller or startup recovery will handle).
            throw;
        }
    }

    /// <summary>
    /// Deletes all staged <c>.tmp</c> files (pre-marker rollback).
    /// </summary>
    private void Rollback()
    {
        foreach (var s in _staged)
        {
            if (s.TmpPath is not null && fs.FileExists(s.TmpPath))
                fs.DeleteFile(s.TmpPath);
        }
    }

    /// <summary>
    /// Recovers from a crash that left a <c>_commit.marker</c> by rolling
    /// forward: completing any remaining renames/deletes.
    /// </summary>
    public static async Task RecoverAsync(IPersistenceFileSystem fs, string markerPath, CancellationToken ct)
    {
        if (!fs.FileExists(markerPath))
            return;

        var json = await fs.ReadAllTextAsync(markerPath, ct);
        var entries = JsonSerializer.Deserialize<List<MarkerEntry>>(json, MarkerJsonOptions);
        if (entries is null || entries.Count == 0)
        {
            fs.DeleteFile(markerPath);
            return;
        }

        var staged = entries.Select(e => new StagedFile(e.TmpPath, e.FinalPath, e.IsDelete)).ToList();
        ExecuteFromMarker(staged, fs);
        fs.DeleteFile(markerPath);
    }

    /// <summary>
    /// Executes renames and deletes from the staged file list.
    /// Idempotent: skips entries where <c>.tmp</c> is already gone (already renamed).
    /// </summary>
    private static void ExecuteFromMarker(List<StagedFile> staged, IPersistenceFileSystem fs)
    {
        foreach (var s in staged)
        {
            if (s.IsDelete)
            {
                if (fs.FileExists(s.FinalPath))
                    fs.DeleteFile(s.FinalPath);
            }
            else if (s.TmpPath is not null && fs.FileExists(s.TmpPath))
            {
                fs.MoveFile(s.TmpPath, s.FinalPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Scans a data directory tree for any <c>_commit.marker</c> files and
    /// rolls them forward. Call at startup before any other I/O.
    /// </summary>
    public static async Task RecoverAllAsync(IPersistenceFileSystem fs, string dataDirectory, CancellationToken ct)
    {
        if (!fs.DirectoryExists(dataDirectory))
            return;

        // Check data directory root.
        var rootMarker = fs.CombinePath(dataDirectory, CommitMarkerFileName);
        await RecoverAsync(fs, rootMarker, ct);

        // Check each entity-type subdirectory.
        foreach (var dir in fs.GetDirectories(dataDirectory))
        {
            var marker = fs.CombinePath(dir, CommitMarkerFileName);
            await RecoverAsync(fs, marker, ct);
        }
    }

    private static readonly JsonSerializerOptions MarkerJsonOptions = new() { WriteIndented = false };

    private sealed record MarkerEntry(string? TmpPath, string FinalPath, bool IsDelete = false);

    internal readonly record struct StagedFile(string? TmpPath, string FinalPath, bool IsDelete = false);
}
