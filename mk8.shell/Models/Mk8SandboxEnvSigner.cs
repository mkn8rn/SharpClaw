using System.Security.Cryptography;
using System.Text;

namespace Mk8.Shell.Models;

/// <summary>
/// Cryptographic signing and verification for sandbox environment files.
/// Uses HMAC-SHA256 with a machine-local key stored in
/// <c>%APPDATA%/mk8.shell/mk8.shell.key</c>.
/// <para>
/// The signed env file (<c>mk8.shell.signed.env</c>) contains the raw
/// env content followed by a newline-separated HMAC signature line.
/// This ensures that env files cannot be copied between machines
/// without re-signing with the local key.
/// </para>
/// </summary>
public static class Mk8SandboxEnvSigner
{
    /// <summary>Separator between env content and the HMAC signature.</summary>
    private const string SignatureSeparator = "\n---MK8-SIGNATURE---\n";

    /// <summary>Key size in bytes (256-bit).</summary>
    private const int KeySizeBytes = 32;

    /// <summary>
    /// Generates a new 256-bit cryptographic key for HMAC signing.
    /// </summary>
    public static byte[] GenerateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySizeBytes);
    }

    /// <summary>
    /// Signs the given env content with the provided key and returns
    /// the full signed file content (env + separator + hex signature).
    /// </summary>
    public static string Sign(string envContent, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(envContent);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"Key must be exactly {KeySizeBytes} bytes.", nameof(key));

        var contentBytes = Encoding.UTF8.GetBytes(envContent);
        var signature = HMACSHA256.HashData(key, contentBytes);
        var signatureHex = Convert.ToHexStringLower(signature);

        return envContent + SignatureSeparator + signatureHex;
    }

    /// <summary>
    /// Verifies that a signed env file was signed with the provided key.
    /// Returns the original env content if valid.
    /// </summary>
    /// <exception cref="Mk8SandboxSignatureException">
    /// Thrown when the signature is missing, malformed, or does not
    /// match the content.
    /// </exception>
    public static string VerifyAndExtract(string signedContent, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedContent);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"Key must be exactly {KeySizeBytes} bytes.", nameof(key));

        var separatorIndex = signedContent.IndexOf(
            SignatureSeparator, StringComparison.Ordinal);

        if (separatorIndex < 0)
            throw new Mk8SandboxSignatureException(
                "Signed env file is missing the signature separator. " +
                "The file may be corrupted or was not signed by mk8.shell.");

        var envContent = signedContent[..separatorIndex];
        var signatureHex = signedContent[
            (separatorIndex + SignatureSeparator.Length)..].Trim();

        if (string.IsNullOrEmpty(signatureHex))
            throw new Mk8SandboxSignatureException(
                "Signed env file has an empty signature.");

        byte[] expectedSignature;
        try
        {
            expectedSignature = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            throw new Mk8SandboxSignatureException(
                "Signed env file has a malformed hex signature.");
        }

        var contentBytes = Encoding.UTF8.GetBytes(envContent);
        var actualSignature = HMACSHA256.HashData(key, contentBytes);

        if (!CryptographicOperations.FixedTimeEquals(
                actualSignature, expectedSignature))
        {
            throw new Mk8SandboxSignatureException(
                "Signature verification failed. The env file was either " +
                "tampered with or signed on a different machine.");
        }

        return envContent;
    }
}

/// <summary>
/// Thrown when sandbox environment signature verification fails.
/// </summary>
public sealed class Mk8SandboxSignatureException(string message)
    : InvalidOperationException($"mk8.shell signature error: {message}");
