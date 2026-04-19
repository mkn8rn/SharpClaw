namespace SharpClaw.Contracts.Persistence;

public sealed class EncryptionOptions
{
    public required byte[] Key { get; init; }

    /// <summary>
    /// When false, <see cref="SharpClaw.Utils.Security.ApiKeyEncryptor"/> is bypassed
    /// for provider API keys, bot tokens, and connection strings — they are stored as
    /// plaintext in the database.
    /// <para>
    /// ⚠️ This is unsafe. Only use for debugging encryption issues.
    /// </para>
    /// </summary>
    public bool EncryptProviderKeys { get; init; } = true;
}
