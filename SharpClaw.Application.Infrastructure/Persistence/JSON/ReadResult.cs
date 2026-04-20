namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Discriminated result type for cold entity read operations.
/// Replaces nullable <c>T?</c> returns so callers can distinguish
/// "entity doesn't exist" from "entity exists but is unreadable."
/// <para>
/// <b>RGAP-15 (Phase F):</b> Enables meaningful diagnostics instead
/// of silent nulls when a file is corrupt or suffers an I/O error.
/// </para>
/// </summary>
/// <typeparam name="T">The deserialized entity type.</typeparam>
public abstract record ReadResult<T>
{
    private ReadResult() { }

    /// <summary>Entity was successfully read and deserialized.</summary>
    /// <param name="Value">The deserialized entity.</param>
    public sealed record Success(T Value) : ReadResult<T>;

    /// <summary>The entity file does not exist on disk.</summary>
    public sealed record NotFound() : ReadResult<T>;

    /// <summary>
    /// The entity file exists but could not be deserialized (corrupt data,
    /// bad encryption envelope, invalid JSON, etc.). The file has been
    /// quarantined to <c>_quarantine/{entityType}/</c>.
    /// </summary>
    /// <param name="Exception">The deserialization or decryption exception.</param>
    /// <param name="FilePath">The original file path before quarantine.</param>
    public sealed record Corrupted(Exception Exception, string FilePath) : ReadResult<T>;

    /// <summary>
    /// A transient I/O error occurred after all retry attempts were
    /// exhausted (e.g., file lock, network storage hiccup).
    /// </summary>
    /// <param name="Exception">The final I/O exception.</param>
    /// <param name="FilePath">The file path that could not be read.</param>
    public sealed record IoError(Exception Exception, string FilePath) : ReadResult<T>;

    /// <summary>
    /// Returns the entity value if this is a <see cref="Success"/> result,
    /// otherwise <c>default</c>.
    /// </summary>
    public T? ValueOrDefault => this is Success s ? s.Value : default;

    /// <summary>
    /// Returns <c>true</c> if this result represents a successful read.
    /// </summary>
    public bool IsSuccess => this is Success;
}
