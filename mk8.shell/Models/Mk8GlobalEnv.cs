using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mk8.Shell.Models;

/// <summary>
/// Global environment loaded from <c>mk8.shell.base.env</c> which ships
/// alongside the assembly. This file is a JSON document containing
/// project bases, git remote URLs, and any other environment-wide
/// settings. It is read once on each initialization/startup/restart and
/// its values are made available globally to the mk8.shell runtime.
/// <para>
/// The file is never written to at runtime — it is a deployment-time
/// configuration artifact.
/// </para>
/// </summary>
public sealed class Mk8GlobalEnv
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Base project names available for <c>dotnet new -n</c> compound
    /// names across all sandboxes.
    /// </summary>
    [JsonPropertyName("ProjectBases")]
    public string[] ProjectBases { get; set; } = [];

    /// <summary>
    /// Allowed git remote URLs across all sandboxes.
    /// </summary>
    [JsonPropertyName("GitRemoteUrls")]
    public string[] GitRemoteUrls { get; set; } = [];

    /// <summary>
    /// Loads the global env from the <c>mk8.shell.base.env</c> file
    /// located next to the executing assembly.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the base env file is not found alongside the assembly.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when the file contains invalid JSON.
    /// </exception>
    public static Mk8GlobalEnv Load()
    {
        var assemblyDir = AppContext.BaseDirectory;
        var envPath = Path.Combine(assemblyDir, "Environment", "mk8.shell.base.env");

        if (!File.Exists(envPath))
            throw new FileNotFoundException(
                "mk8.shell.base.env not found. Ensure the file is " +
                "copied to the output directory.", envPath);

        var json = File.ReadAllText(envPath);
        return JsonSerializer.Deserialize<Mk8GlobalEnv>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                "mk8.shell.base.env deserialized to null.");
    }

    /// <summary>
    /// Builds an <see cref="Mk8RuntimeConfig"/> from the loaded global env.
    /// </summary>
    public Mk8RuntimeConfig ToRuntimeConfig() => new()
    {
        ProjectBases = ProjectBases,
        GitRemoteUrls = GitRemoteUrls,
    };
}
