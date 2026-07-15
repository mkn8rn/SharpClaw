using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpClaw.Runtime.INF.DurableStorage;

/// <summary>Authenticates provider-neutral relational keyset cursors.</summary>
public sealed class DatabaseCursorCodec(byte[] key)
{
    private const int Version = 1;
    private readonly byte[] _key = ValidateKey(key);

    public string Encode(
        string scope,
        DateTimeOffset createdAt,
        Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(Version, scope, createdAt, id));
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    public DatabaseKeysetCursor Decode(string cursor, string expectedScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedScope);
        var separator = cursor.IndexOf('.');
        if (separator <= 0 || separator == cursor.Length - 1)
            throw new InvalidOperationException("The cursor is malformed.");

        var payload = FromBase64Url(cursor[..separator]);
        var signature = FromBase64Url(cursor[(separator + 1)..]);
        var expected = HMACSHA256.HashData(_key, payload);
        if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            throw new InvalidOperationException("The cursor signature is invalid.");

        var decoded = JsonSerializer.Deserialize<CursorPayload>(payload)
            ?? throw new InvalidOperationException("The cursor payload is invalid.");
        if (decoded.Version != Version
            || !string.Equals(
                decoded.Scope,
                expectedScope,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The cursor does not belong to this query.");
        }

        return new DatabaseKeysetCursor(decoded.CreatedAt, decoded.Id);
    }

    private static byte[] ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
            throw new ArgumentException("Cursor key must contain at least 32 bytes.");
        return key.ToArray();
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new InvalidOperationException("The cursor is malformed."),
        };
        return Convert.FromBase64String(normalized);
    }

    private sealed record CursorPayload(
        int Version,
        string Scope,
        DateTimeOffset CreatedAt,
        Guid Id);
}

public sealed record DatabaseKeysetCursor(DateTimeOffset CreatedAt, Guid Id);
