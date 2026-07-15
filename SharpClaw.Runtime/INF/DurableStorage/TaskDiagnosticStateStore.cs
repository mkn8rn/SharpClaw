using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Runtime.INF.DurableStorage;

/// <summary>
/// Stores bounded light task diagnostics in an encrypted atomic manifest and
/// externalizes each large shared-data entry as its own encrypted artifact.
/// Updating one entry never serializes or rewrites the content of another.
/// </summary>
public sealed class TaskDiagnosticStateStore
{
    private static readonly byte[] Magic = "SCSTATE2"u8.ToArray();
    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int PreviewCharacters = 2048;
    private const int MaxLightDataCharacters = 32_768;
    private const int MaxBigDataCharacters = 200_000;
    private const int MaxBigDataEntries = 1000;
    private const int MaxBigDataIdCharacters = 128;
    private const int MaxBigDataTitleCharacters = 512;
    private readonly string _root;
    private readonly byte[] _key;
    private readonly IExecutionArtifactStore _artifacts;
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public TaskDiagnosticStateStore(
        string durableRoot,
        byte[] encryptionKey,
        IExecutionArtifactStore artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(durableRoot);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(artifacts);
        _root = Path.GetFullPath(Path.Combine(durableRoot, "task-state"));
        _key = encryptionKey.ToArray();
        _artifacts = artifacts;
        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "Task diagnostic state encryption requires a 32-byte key.");
        }

        Directory.CreateDirectory(_root);
    }

    public async Task ApplyChangeAsync(
        Guid instanceId,
        TaskDiagnosticStateChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var gate = GetGate(instanceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(instanceId, cancellationToken)
                .ConfigureAwait(false)
                ?? TaskDiagnosticState.Empty(instanceId);
            var entries = current.BigData.ToDictionary(
                entry => entry.Id,
                StringComparer.Ordinal);
            switch (change.Kind)
            {
                case TaskDiagnosticStateChangeKind.LightDataReplaced:
                    if (change.LightData is { Length: > MaxLightDataCharacters })
                    {
                        throw new InvalidOperationException(
                            "Task light diagnostic state exceeds its character limit.");
                    }

                    current = current with { LightData = change.LightData };
                    break;

                case TaskDiagnosticStateChangeKind.BigDataUpserted:
                    var bigData = change.BigData
                        ?? throw new InvalidOperationException(
                            "A big-data upsert requires an entry payload.");
                    ValidateBigData(bigData);
                    await using (var content = new MemoryStream(
                                     Encoding.UTF8.GetBytes(bigData.Content),
                                     writable: false))
                    {
                        var descriptor = await _artifacts.PutAsync(
                                content,
                                new ArtifactWriteRequest(
                                    ExecutionOwnerKind.TaskInstance,
                                    instanceId,
                                    "text/plain; charset=utf-8",
                                    BoundPreview(bigData.Content)),
                                cancellationToken)
                            .ConfigureAwait(false);
                        entries[bigData.Id] = new TaskBigDataState(
                            bigData.Id,
                            bigData.Title,
                            bigData.CreatedAt,
                            descriptor);
                    }
                    break;

                case TaskDiagnosticStateChangeKind.BigDataRemoved:
                    if (string.IsNullOrWhiteSpace(change.BigDataId))
                    {
                        throw new InvalidOperationException(
                            "A big-data removal requires an entry identifier.");
                    }

                    entries.Remove(change.BigDataId);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(change.Kind));
            }

            var updated = current with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                BigData = entries.Values
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .ToArray(),
            };
            await WriteCoreAsync(updated, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TaskDiagnosticState?> ReadAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetGate(instanceId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(instanceId, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HashSet<Guid>> ReadReferencedArtifactIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new HashSet<Guid>();
        foreach (var path in Directory.EnumerateFiles(
                     _root,
                     "*.scstate",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var instanceId))
                throw new InvalidDataException("Task state file name is invalid.");
            var state = await ReadAsync(instanceId, cancellationToken)
                .ConfigureAwait(false);
            if (state is null)
                continue;
            foreach (var entry in state.BigData)
                result.Add(entry.Artifact.Id);
        }

        return result;
    }

    public async Task<TaskStateRetentionResult> ApplyRetentionAsync(
        IReadOnlySet<Guid> protectedInstanceIds,
        TimeSpan maximumAge,
        int maximumDeletes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedInstanceIds);
        if (maximumAge <= TimeSpan.Zero || maximumDeletes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumAge));

        var cutoff = DateTimeOffset.UtcNow - maximumAge;
        var candidates = Directory.EnumerateFiles(
                _root,
                "*.scstate",
                SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc <= cutoff.UtcDateTime)
            .Select(file => new
            {
                File = file,
                Parsed = Guid.TryParse(
                    Path.GetFileNameWithoutExtension(file.Name),
                    out var instanceId),
                InstanceId = instanceId,
            })
            .Where(candidate => candidate.Parsed
                && !protectedInstanceIds.Contains(candidate.InstanceId))
            .OrderBy(candidate => candidate.File.LastWriteTimeUtc)
            .Take(maximumDeletes)
            .ToArray();
        var deleted = 0;
        long reclaimed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gate = GetGate(candidate.InstanceId);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(candidate.File.FullName)
                    || protectedInstanceIds.Contains(candidate.InstanceId)
                    || File.GetLastWriteTimeUtc(candidate.File.FullName)
                        > cutoff.UtcDateTime)
                {
                    continue;
                }

                var length = new FileInfo(candidate.File.FullName).Length;
                File.Delete(candidate.File.FullName);
                reclaimed = checked(reclaimed + length);
                deleted++;
            }
            catch (IOException)
            {
            }
            finally
            {
                gate.Release();
            }
        }

        return new TaskStateRetentionResult(deleted, reclaimed);
    }

    private async Task<TaskDiagnosticState?> ReadCoreAsync(
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(instanceId);
        if (!File.Exists(path))
            return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        var headerLength = Magic.Length + 12 + 16;
        if (bytes.Length < headerLength
            || bytes.Length > MaxManifestBytes + headerLength
            || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Task diagnostic state header is invalid.");
        }

        var nonce = bytes.AsSpan(Magic.Length, 12);
        var tag = bytes.AsSpan(Magic.Length + 12, 16);
        var ciphertext = bytes.AsSpan(headerLength);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                BuildAssociatedData(instanceId));
        }

        var state = JsonSerializer.Deserialize<TaskDiagnosticState>(plaintext)
            ?? throw new InvalidDataException(
                "Task diagnostic state payload is invalid.");
        ValidateState(state, instanceId);
        return state;
    }

    private async Task WriteCoreAsync(
        TaskDiagnosticState state,
        CancellationToken cancellationToken)
    {
        ValidateState(state, state.InstanceId);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
        if (plaintext.Length > MaxManifestBytes)
        {
            throw new InvalidOperationException(
                "Task diagnostic state manifest exceeds its size limit.");
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag,
                BuildAssociatedData(state.InstanceId));
        }

        var target = GetPath(state.InstanceId);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private string GetPath(Guid instanceId)
    {
        if (instanceId == Guid.Empty)
            throw new ArgumentException("Task instance ID is required.", nameof(instanceId));
        var path = Path.GetFullPath(
            Path.Combine(_root, $"{instanceId:D}.scstate"));
        var boundary = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Task state path escaped its root.");
        return path;
    }

    private SemaphoreSlim GetGate(Guid instanceId) =>
        _gates[(int)((uint)instanceId.GetHashCode() % (uint)_gates.Length)];

    private static byte[] BuildAssociatedData(Guid instanceId)
    {
        var associatedData = new byte[Magic.Length + 16];
        Magic.CopyTo(associatedData, 0);
        instanceId.TryWriteBytes(associatedData.AsSpan(Magic.Length));
        return associatedData;
    }

    private static void ValidateState(TaskDiagnosticState state, Guid instanceId)
    {
        if (state.Version != 2 || state.InstanceId != instanceId)
            throw new InvalidDataException("Task diagnostic state identity is invalid.");
        if (state.LightData is { Length: > MaxLightDataCharacters }
            || state.BigData.Count > MaxBigDataEntries
            || state.BigData.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count()
                != state.BigData.Count)
        {
            throw new InvalidDataException("Task diagnostic state bounds are invalid.");
        }

        foreach (var entry in state.BigData)
        {
            if (entry.Id.Length is 0 or > MaxBigDataIdCharacters
                || entry.Title.Length > MaxBigDataTitleCharacters
                || entry.Artifact.OwnerKind != ExecutionOwnerKind.TaskInstance
                || entry.Artifact.OwnerId != instanceId)
            {
                throw new InvalidDataException(
                    "Task diagnostic big-data descriptor is invalid.");
            }
        }
    }

    private static void ValidateBigData(TaskDiagnosticBigDataChange entry)
    {
        if (entry.Id.Length is 0 or > MaxBigDataIdCharacters
            || entry.Title.Length > MaxBigDataTitleCharacters
            || entry.Content.Length > MaxBigDataCharacters)
        {
            throw new InvalidOperationException(
                "Task big-data entry exceeds its configured bounds.");
        }
    }

    private static string BoundPreview(string value) =>
        value.Length <= PreviewCharacters
            ? value
            : value[..PreviewCharacters];
}

public sealed record TaskDiagnosticState(
    int Version,
    Guid InstanceId,
    DateTimeOffset UpdatedAt,
    string? LightData,
    IReadOnlyList<TaskBigDataState> BigData)
{
    public static TaskDiagnosticState Empty(Guid instanceId) =>
        new(2, instanceId, DateTimeOffset.UtcNow, null, []);
}

public sealed record TaskBigDataState(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    ExecutionArtifactDescriptor Artifact);

public sealed record TaskStateRetentionResult(
    int DeletedStates,
    long ReclaimedBytes);

public sealed record TaskDiagnosticStateChange(
    TaskDiagnosticStateChangeKind Kind,
    string? LightData = null,
    TaskDiagnosticBigDataChange? BigData = null,
    string? BigDataId = null);

public enum TaskDiagnosticStateChangeKind
{
    LightDataReplaced,
    BigDataUpserted,
    BigDataRemoved,
}

public sealed record TaskDiagnosticBigDataChange(
    string Id,
    string Title,
    string Content,
    DateTimeOffset CreatedAt);
