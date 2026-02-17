using System.Security.Cryptography;
using System.Text;

namespace SharpClaw.Utils.Security;

public static class ApiKeyEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Encrypt(string plainText, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Format: nonce + ciphertext + tag
        var result = new byte[NonceSize + cipherBytes.Length + TagSize];
        nonce.CopyTo(result, 0);
        cipherBytes.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string encryptedText, byte[] key)
    {
        var data = Convert.FromBase64String(encryptedText);

        var nonce = data[..NonceSize];
        var tag = data[^TagSize..];
        var cipherBytes = data[NonceSize..^TagSize];
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(32);
}
