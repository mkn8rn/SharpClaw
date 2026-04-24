using Microsoft.Extensions.DependencyInjection;

namespace SharpClaw.Modules.DatabaseAccess.Services;

/// <summary>
/// Executes a raw SQL query against a user-configured external database.
/// </summary>
internal sealed class DatabaseQueryExecutor(IServiceScopeFactory scopeFactory) : IDatabaseQueryExecutor
{
    public async Task<bool> HasRowsAsync(string sql, CancellationToken ct)
    {
        // TODO: resolve connection string and database type from context,
        // then dispatch to the appropriate ADO.NET provider.
        await Task.CompletedTask;
        throw new NotSupportedException(
            "HasRowsAsync requires a connection string and database type. " +
            "Update IDatabaseQueryExecutor to include connection parameters.");
    }
}
