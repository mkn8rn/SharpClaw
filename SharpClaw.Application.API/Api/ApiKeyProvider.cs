using System.Security.Cryptography;

namespace SharpClaw.Application.API.Api;

/// <summary>
/// Generates and manages a per-session API key written to a file
/// with restrictive permissions. Only processes that can read this file
/// (i.e., running as the same user) can authenticate with the localhost API.
/// </summary>
public sealed class ApiKeyProvider
{
    public string ApiKey { get; }
    public string KeyFilePath { get; }

    public ApiKeyProvider()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpClaw");

        Directory.CreateDirectory(directory);

        KeyFilePath = Path.Combine(directory, ".api-key");
        ApiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        WriteKeyFileSecure();
    }

    private void WriteKeyFileSecure()
    {
        File.WriteAllText(KeyFilePath, ApiKey);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(KeyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Cleanup()
    {
        try { File.Delete(KeyFilePath); }
        catch { /* best-effort */ }
    }
}
