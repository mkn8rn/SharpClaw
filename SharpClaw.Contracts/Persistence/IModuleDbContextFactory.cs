namespace SharpClaw.Contracts.Persistence;

/// <summary>
/// Factory boundary used by modules to request host-configured instances of
/// their own persistence context types.
/// </summary>
public interface IModuleDbContextFactory
{
    /// <summary>
    /// Creates a module-owned context instance for the specified context type.
    /// The returned instance is owned by the caller and must be disposed.
    /// </summary>
    object CreateDbContext(Type dbContextType);

    /// <summary>
    /// Creates a module-owned context instance for the specified context type.
    /// The returned instance is owned by the caller and must be disposed.
    /// </summary>
    TContext CreateDbContext<TContext>()
        where TContext : class, IDisposable
        => (TContext)CreateDbContext(typeof(TContext));
}
