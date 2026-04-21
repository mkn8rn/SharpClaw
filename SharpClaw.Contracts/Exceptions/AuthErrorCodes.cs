namespace SharpClaw.Contracts.Exceptions;

/// <summary>
/// Machine-readable error codes returned in the JSON body of authentication failure responses.
/// <list type="bullet">
///   <item><term><see cref="InvalidApiKey"/></term><description>HTTP 423 — the X-Api-Key header is missing or wrong (process-level lock).</description></item>
///   <item><term><see cref="InvalidAccessToken"/></term><description>HTTP 401 — the Bearer token is absent, malformed, or has an invalid signature.</description></item>
///   <item><term><see cref="AccessTokenExpiredException.ErrorCode"/></term><description>HTTP 419 — the Bearer token was valid but has expired or was server-side invalidated.</description></item>
/// </list>
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// The <c>X-Api-Key</c> header is missing or the key does not match the current session key.
    /// This is a process-level trust failure — the caller cannot read the local key file.
    /// Returned with HTTP 423 Locked.
    /// </summary>
    public const string InvalidApiKey = "invalid_api_key";

    /// <summary>
    /// The <c>Authorization: Bearer</c> token is absent, structurally invalid, or its
    /// signature does not verify against the current signing key.
    /// Distinct from an expired token, which uses <see cref="AccessTokenExpiredException.ErrorCode"/>.
    /// Returned with HTTP 401 Unauthorized.
    /// </summary>
    public const string InvalidAccessToken = "invalid_access_token";
}
