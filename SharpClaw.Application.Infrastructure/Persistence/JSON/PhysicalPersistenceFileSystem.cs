using System.Buffers;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Production implementation of <see cref="IPersistenceFileSystem"/> that
/// delegates to <see cref="System.IO"/> with pool-backed reads.
/// </summary>
public sealed class PhysicalPersistenceFileSystem : IPersistenceFileSystem
{
    public async Task<OwnedMemory> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);

        var length = (int)stream.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), ct);
                if (read == 0)
                    break;
                offset += read;
            }

            return new OwnedMemory(buffer, offset);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        await stream.WriteAsync(data, ct);
    }

    public Task WriteAllTextAsync(string path, string text, CancellationToken ct = default)
        => File.WriteAllTextAsync(path, text, ct);

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
        => File.ReadAllTextAsync(path, ct);

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = true)
        => File.Move(sourcePath, destinationPath, overwrite);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string[] GetFiles(string directoryPath, string searchPattern)
        => Directory.GetFiles(directoryPath, searchPattern);

    public string[] GetDirectories(string directoryPath)
        => Directory.GetDirectories(directoryPath);

    public string CombinePath(params ReadOnlySpan<string> segments)
        => Path.Combine(segments);

    public string GetFileName(string path) => Path.GetFileName(path);

    public async Task FlushFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1, useAsync: true);
        await stream.FlushAsync(ct);
    }

    public Task AppendAllTextAsync(string path, string text, CancellationToken ct = default)
        => File.AppendAllTextAsync(path, text, ct);
}
