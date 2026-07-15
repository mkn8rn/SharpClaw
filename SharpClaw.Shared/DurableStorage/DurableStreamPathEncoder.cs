using System.Security.Cryptography;
using System.Text;

namespace SharpClaw.Shared.DurableStorage;

public sealed class DurableStreamPathEncoder(string rootDirectory)
{
    private readonly string _root = Path.GetFullPath(
        string.IsNullOrWhiteSpace(rootDirectory)
            ? throw new ArgumentException("Root directory is required.", nameof(rootDirectory))
            : rootDirectory);

    public string GetStreamDirectory(DurableStreamKey key)
    {
        if (string.IsNullOrWhiteSpace(key.CanonicalValue))
            throw new ArgumentException("A typed stream key is required.", nameof(key));

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key.CanonicalValue)))
            .ToLowerInvariant();
        var kind = key.Kind.ToString().ToLowerInvariant();
        var candidate = Path.GetFullPath(
            Path.Combine(_root, "streams", kind, hash[..2], hash));
        var boundary = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved stream path escaped the durable root.");
        return candidate;
    }

    public string GetStreamHash(DurableStreamKey key) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(key.CanonicalValue)))
            .ToLowerInvariant();
}
