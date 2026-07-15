using System.Security.Cryptography;
using System.Text;

namespace SharpClaw.Shared.DurableStorage;

public static class DurableStorageKeyDerivation
{
    public static byte[] Derive(byte[] rootKey, string purpose)
    {
        ArgumentNullException.ThrowIfNull(rootKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (rootKey.Length != 32)
            throw new ArgumentException("Root key must contain 256 bits.", nameof(rootKey));

        return HMACSHA256.HashData(
            rootKey,
            Encoding.UTF8.GetBytes($"SharpClaw/durable/v1/{purpose}"));
    }
}
