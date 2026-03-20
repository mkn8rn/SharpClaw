using System.Diagnostics;

namespace SharpClaw.Services;

/// <summary>
/// Manages the lifecycle of the SharpClaw API backend process.
/// <para>
/// <b>Bundled mode</b> (distribution): the API exe lives in a
/// <c>backend</c> subfolder relative to the app's base directory.
/// This manager starts and stops it automatically.
/// </para>
/// <para>
/// <b>External mode</b> (development): the API is started separately
/// (e.g. via F5 in Visual Studio). This manager detects it through
/// <see cref="IsApiReachableAsync"/> and skips process launch.
/// </para>
/// </summary>
public sealed class BackendProcessManager : IDisposable
{
    private Process? _process;
    private readonly string _executablePath;
    private string _apiUrl;
    private readonly List<string> _processOutput = [];
    private readonly object _outputLock = new();

    /// <summary>
    /// <c>true</c> when we confirmed the API is reachable but was not
    /// started by this manager (i.e. an external/dev-time instance).
    /// </summary>
    public bool IsExternal { get; private set; }

    /// <summary>Current API base URL.</summary>
    public string ApiUrl => _apiUrl;

    /// <summary>
    /// All stdout + stderr lines captured from the bundled process
    /// (most recent last). Thread-safe snapshot.
    /// </summary>
    public IReadOnlyList<string> ProcessOutput
    {
        get { lock (_outputLock) return [.. _processOutput]; }
    }

    /// <summary>Exit code of the bundled process, or <c>null</c> if still running or never started.</summary>
    public int? ExitCode => _process is { HasExited: true } p ? p.ExitCode : null;

    public BackendProcessManager(string apiUrl)
    {
        _apiUrl = apiUrl;

        var baseDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows()
            ? "SharpClaw.Application.API.exe"
            : "SharpClaw.Application.API";

        _executablePath = Path.Combine(baseDir, "backend", exeName);
    }

    /// <summary>
    /// Changes the target API URL. Only meaningful before the next
    /// <see cref="EnsureStartedAsync"/> call (does not restart a
    /// running bundled process).
    /// </summary>
    public void UpdateApiUrl(string apiUrl) => _apiUrl = apiUrl;

    /// <summary>
    /// Returns <c>true</c> if the bundled backend executable exists on disk.
    /// </summary>
    public bool IsAvailable => File.Exists(_executablePath);

    /// <summary>
    /// Returns <c>true</c> if the backend process was started by this
    /// manager and is still running.
    /// </summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Probes the API's <c>/echo</c> endpoint (unauthenticated) to check
    /// whether an instance is already listening — typically the case
    /// during development when the API project is started separately.
    /// </summary>
    public async Task<bool> IsApiReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync($"{_apiUrl}/echo", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures the API backend is available. Checks in order:
    /// <list type="number">
    ///   <item>Is the API already reachable? → mark external, done.</item>
    ///   <item>Is the bundled backend available? → launch it.</item>
    ///   <item>Neither → throw.</item>
    /// </list>
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return;

        // Check if an external instance is already serving (dev workflow).
        if (await IsApiReachableAsync(ct))
        {
            IsExternal = true;
            return;
        }

        if (!IsAvailable)
            throw new InvalidOperationException(
                "SharpClaw API is not reachable and no bundled backend was found. " +
                "Start the API project manually or publish with /p:BundleBackend=true.");

        Start();
    }

    /// <summary>
    /// Starts the bundled API backend process.
    /// </summary>
    public void Start()
    {
        if (IsRunning)
            return;

        if (!IsAvailable)
            throw new FileNotFoundException(
                $"SharpClaw API backend not found at '{_executablePath}'. " +
                "Ensure the backend is published into the 'backend' subfolder.");

        // Clear stale output from any previous run.
        lock (_outputLock) _processOutput.Clear();
        IsExternal = false;

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_executablePath)!,
        };

        // Pass the URL so the API binds to the expected port.
        psi.EnvironmentVariables["ASPNETCORE_URLS"] = _apiUrl;

        // Redirect the data directory to a writable location.
        // Inside MSIX, the install folder (C:\Program Files\WindowsApps\...) is
        // read-only, so JSON persistence and logs must go to %LOCALAPPDATA%.
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpClaw", "Data");
        psi.EnvironmentVariables["SHARPCLAW_DATA_DIR"] = dataDir;

        _process = Process.Start(psi);

        // Consume stdout/stderr asynchronously to prevent pipe-buffer
        // deadlock — ASP.NET Core writes startup logs that fill the OS
        // buffer and block the process before Kestrel binds the port.
        _process!.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_outputLock) _processOutput.Add(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_outputLock) _processOutput.Add($"[stderr] {e.Data}");
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>
    /// Gracefully stops the backend process. No-op if the API was
    /// started externally (we don't own that process).
    /// </summary>
    public void Stop()
    {
        if (IsExternal || _process is null or { HasExited: true })
            return;

        try
        {
            if (!_process.CloseMainWindow())
                _process.Kill(entireProcessTree: true);

            _process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
    }
}
