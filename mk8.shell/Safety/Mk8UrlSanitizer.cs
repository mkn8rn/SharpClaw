namespace Mk8.Shell.Safety;

/// <summary>
/// SSRF protection for mk8.shell HTTP verbs.
/// </summary>
public static class Mk8UrlSanitizer
{
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "https", "http" };

    private static readonly HashSet<string> BlockedHosts =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "metadata.google.internal",
        "metadata.internal",
        "169.254.169.254",
    };

    private static readonly HashSet<int> AllowedPorts = [80, 443, -1];

    public static Uri Validate(string rawUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawUrl);

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            throw new Mk8UrlViolationException(rawUrl, "Not a valid absolute URL.");

        if (!AllowedSchemes.Contains(uri.Scheme))
            throw new Mk8UrlViolationException(rawUrl,
                $"Scheme '{uri.Scheme}' is not allowed. Use https or http.");

        if (BlockedHosts.Contains(uri.Host))
            throw new Mk8UrlViolationException(rawUrl,
                $"Host '{uri.Host}' is blocked.");

        if (!AllowedPorts.Contains(uri.Port))
            throw new Mk8UrlViolationException(rawUrl,
                $"Port {uri.Port} is not allowed. Use 80 or 443.");

        if (uri.UserInfo.Length > 0)
            throw new Mk8UrlViolationException(rawUrl,
                "URLs with embedded credentials are not allowed.");

        return uri;
    }

    public static bool IsPrivateOrReserved(System.Net.IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIPv4(bytes),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => IsPrivateIPv6(bytes),
            _ => true
        };
    }

    private static bool IsPrivateIPv4(byte[] bytes) =>
        bytes[0] == 10
        || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        || (bytes[0] == 192 && bytes[1] == 168)
        || (bytes[0] == 169 && bytes[1] == 254)
        || bytes[0] == 127
        || bytes[0] == 0;

    private static bool IsPrivateIPv6(byte[] bytes) =>
        bytes[0] == 0xFD
        || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
        || bytes.All(b => b == 0)
        || (bytes.Take(15).All(b => b == 0) && bytes[15] == 1);
}
