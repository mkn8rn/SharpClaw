using System.Collections.Concurrent;
using System.Text;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// In-memory implementation of <see cref="IPersistenceFileSystem"/> for
/// unit tests and fault-injection scenarios. All data is stored in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class InMemoryPersistenceFileSystem : IPersistenceFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional callback invoked before reads. Throw to simulate I/O failures.
    /// </summary>
    public Func<string, Task>? OnBeforeRead { get; set; }

    /// <summary>
    /// Optional callback invoked before writes. Throw to simulate I/O failures.
    /// </summary>
    public Func<string, Task>? OnBeforeWrite { get; set; }

    public async Task<OwnedMemory> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (OnBeforeRead is not null)
            await OnBeforeRead(path);

        if (!_files.TryGetValue(Normalize(path), out var data))
            throw new FileNotFoundException("File not found in InMemoryPersistenceFileSystem.", path);

        return OwnedMemory.WrapUnpooled(data.ToArray());
    }

    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (OnBeforeWrite is not null)
            await OnBeforeWrite(path);

        _files[Normalize(path)] = data.ToArray();
        EnsureParentDirectory(path);
    }

    public async Task WriteAllTextAsync(string path, string text, CancellationToken ct = default)
    {
        if (OnBeforeWrite is not null)
            await OnBeforeWrite(path);

        _files[Normalize(path)] = Encoding.UTF8.GetBytes(text);
        EnsureParentDirectory(path);
    }

    public async Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        if (OnBeforeRead is not null)
            await OnBeforeRead(path);

        if (!_files.TryGetValue(Normalize(path), out var data))
            throw new FileNotFoundException("File not found in InMemoryPersistenceFileSystem.", path);

        return Encoding.UTF8.GetString(data);
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public void DeleteFile(string path) => _files.TryRemove(Normalize(path), out _);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = true)
    {
        var src = Normalize(sourcePath);
        var dst = Normalize(destinationPath);

        if (!_files.TryRemove(src, out var data))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        if (!overwrite && _files.ContainsKey(dst))
            throw new IOException($"Destination file already exists: {destinationPath}");

        _files[dst] = data;
        EnsureParentDirectory(destinationPath);
    }

    public bool DirectoryExists(string path) => _directories.ContainsKey(Normalize(path));

    public void CreateDirectory(string path)
    {
        var normalized = Normalize(path);
        _directories[normalized] = 0;

        // Create parent chain
        var parent = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(parent))
        {
            _directories[Normalize(parent)] = 0;
            parent = Path.GetDirectoryName(parent);
        }
    }

    public string[] GetFiles(string directoryPath, string searchPattern)
    {
        var dir = Normalize(directoryPath);
        var extension = searchPattern.StartsWith('*') ? searchPattern[1..] : null;

        return _files.Keys
            .Where(k =>
            {
                var kDir = Normalize(Path.GetDirectoryName(k) ?? "");
                if (!string.Equals(kDir, dir, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (extension is not null)
                    return k.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
                return true;
            })
            .ToArray();
    }

    public string CombinePath(params ReadOnlySpan<string> segments) => Path.Combine(segments);

    public string[] GetDirectories(string directoryPath)
    {
        var dir = Normalize(directoryPath);
        return _directories.Keys
            .Where(k =>
            {
                if (k.Length <= dir.Length) return false;
                var parent = Normalize(Path.GetDirectoryName(k) ?? "");
                return string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
    }

    public string GetFileName(string path) => Path.GetFileName(path);

    /// <summary>No-op for in-memory; records the call for test assertions.</summary>
    public Task FlushFileAsync(string path, CancellationToken ct = default)
    {
        FsyncedFiles.Add(Normalize(path));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Paths that were fsynced — useful for test assertions.
    /// </summary>
    public List<string> FsyncedFiles { get; } = [];

    /// <summary>
    /// Returns all stored file paths (normalized) — useful for test assertions.
    /// </summary>
    public IReadOnlyCollection<string> AllFiles => _files.Keys.ToArray();

    /// <summary>
    /// Returns all stored directory paths (normalized) — useful for test assertions.
    /// </summary>
    public IReadOnlyCollection<string> AllDirectories => _directories.Keys.ToArray();

    public async Task AppendAllTextAsync(string path, string text, CancellationToken ct = default)
    {
        var normalized = Normalize(path);
        EnsureParentDirectory(path);
        string existing = "";
        if (_files.TryGetValue(normalized, out var data))
            existing = Encoding.UTF8.GetString(data);
        await WriteAllTextAsync(path, existing + text, ct);
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    private void EnsureParentDirectory(string filePath)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent))
            CreateDirectory(parent);
    }
}
