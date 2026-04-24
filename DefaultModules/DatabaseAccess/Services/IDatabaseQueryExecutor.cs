namespace SharpClaw.Modules.DatabaseAccess.Services;

/// <summary>
/// Executes a raw SQL query and reports whether any rows were returned.
/// </summary>
public interface IDatabaseQueryExecutor
{
    /// <summary>
    /// Returns <see langword="true"/> if the supplied <paramref name="sql"/> produces at least one row.
    /// </summary>
    Task<bool> HasRowsAsync(string sql, CancellationToken ct);
}
