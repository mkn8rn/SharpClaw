namespace SharpClaw.Modules.DatabaseAccess.Enums;

/// <summary>
/// Identifies the database engine type for external or internal database
/// resources.  Determines the query language and connection driver used
/// when executing queries against the database.
/// </summary>
public enum DatabaseType
{
    MySQL = 0,
    PostgreSQL = 1,
    SQLite = 2,
    MSSQL = 3,
    MongoDB = 4,
    Redis = 5,
    CosmosDB = 6,
    MariaDB = 7,
    Oracle = 8,
    CockroachDB = 9,
    Firebird = 10,

    /// <summary>
    /// Custom / user-supplied driver configuration.
    /// </summary>
    Custom = 99,
}
