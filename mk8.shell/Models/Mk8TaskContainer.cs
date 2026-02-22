using Mk8.Shell.Engine;

namespace Mk8.Shell.Models;

/// <summary>
/// An isolated, disposable execution envelope for a single mk8.shell
/// command. Created fresh for every command, disposed immediately after
/// — no data transfers between commands.
/// <para>
/// Lifecycle for every command execution:
/// <list type="number">
///   <item>Load global env from <c>mk8.shell.base.env</c>.</item>
///   <item>Look up sandbox ID in local <c>%APPDATA%/mk8.shell/sandboxes.json</c>.</item>
///   <item>Resolve sandbox root path.</item>
///   <item>Read <c>mk8.shell.signed.env</c> from sandbox root.</item>
///   <item>Verify signature against local key.</item>
///   <item>Extract env vars from the verified signed content.</item>
///   <item>Build <see cref="Mk8WorkspaceContext"/> with merged env.</item>
///   <item>Execute the command.</item>
///   <item>Dispose — all state is discarded.</item>
/// </list>
/// </para>
/// </summary>
public sealed class Mk8TaskContainer : IDisposable
{
    /// <summary>Sandbox identity for this task.</summary>
    public Mk8Sandbox Sandbox { get; }

    /// <summary>
    /// Workspace context built from global + sandbox env.
    /// Used by the compiler and executor.
    /// </summary>
    public Mk8WorkspaceContext Workspace { get; }

    /// <summary>
    /// Runtime config built from global env. Used to construct the
    /// command whitelist for this task.
    /// </summary>
    public Mk8RuntimeConfig RuntimeConfig { get; }

    private bool _disposed;

    private Mk8TaskContainer(
        Mk8Sandbox sandbox,
        Mk8WorkspaceContext workspace,
        Mk8RuntimeConfig runtimeConfig)
    {
        Sandbox = sandbox;
        Workspace = workspace;
        RuntimeConfig = runtimeConfig;
    }

    /// <summary>
    /// Creates an isolated task container for the given sandbox ID.
    /// Performs the full initialization sequence: global env → registry
    /// lookup → signature verification → env loading.
    /// </summary>
    /// <param name="sandboxId">
    /// The sandbox identifier (e.g. "Banana"). Must match a registered
    /// sandbox in the local <c>%APPDATA%/mk8.shell</c> registry.
    /// </param>
    /// <param name="registry">
    /// Local sandbox registry. If <c>null</c>, uses the default
    /// <c>%APPDATA%/mk8.shell</c> location.
    /// </param>
    /// <exception cref="Mk8SandboxNotFoundException">
    /// Thrown when the sandbox ID is not registered on this machine.
    /// </exception>
    /// <exception cref="Mk8SandboxSignatureException">
    /// Thrown when the signed env file is missing, corrupted, or was
    /// signed on a different machine.
    /// </exception>
    public static Mk8TaskContainer Create(
        string sandboxId,
        Mk8SandboxRegistry? registry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);

        // Step 1: Load global env.
        var globalEnv = Mk8GlobalEnv.Load();
        var runtimeConfig = globalEnv.ToRuntimeConfig();

        // Step 2: Resolve sandbox from local registry.
        registry ??= new Mk8SandboxRegistry();
        var entry = registry.Resolve(sandboxId);

        var sandboxRoot = Path.GetFullPath(entry.RootPath);
        if (!Directory.Exists(sandboxRoot))
            throw new DirectoryNotFoundException(
                $"Sandbox root directory '{sandboxRoot}' for sandbox " +
                $"'{sandboxId}' does not exist.");

        // Step 3: Load and verify signed env.
        var signedEnvPath = Path.Combine(
            sandboxRoot, Mk8SandboxRegistry.SandboxSignedEnvFileName);

        if (!File.Exists(signedEnvPath))
            throw new Mk8SandboxSignatureException(
                $"Signed env file not found at '{signedEnvPath}'. " +
                "The sandbox may not have been initialized by mk8.shell.startup.");

        var key = registry.LoadKey();
        var signedContent = File.ReadAllText(signedEnvPath);
        var envContent = Mk8SandboxEnvSigner.VerifyAndExtract(signedContent, key);

        // Step 4: Parse env vars from verified content.
        var sandboxVars = Mk8SandboxEnvParser.Parse(envContent);

        // Step 5: Build sandbox model.
        var sandbox = new Mk8Sandbox
        {
            Id = sandboxId,
            RootPath = sandboxRoot,
            RegisteredAtUtc = entry.RegisteredAtUtc,
        };

        // Step 6: Build workspace context.
        // Sandbox env vars are loaded as additional variables. The
        // sandbox root becomes $WORKSPACE, and working directory
        // defaults to the sandbox root.
        var workspace = new Mk8WorkspaceContext(
            SandboxId: sandboxId,
            SandboxRoot: sandboxRoot,
            WorkingDirectory: sandboxRoot,
            RunAsUser: Environment.UserName,
            Variables: sandboxVars);

        return new Mk8TaskContainer(sandbox, workspace, runtimeConfig);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No unmanaged resources — the point of Dispose is to make
        // the "create → use → discard" lifecycle explicit and to
        // guarantee that no reference to sandbox state leaks out.
    }
}
