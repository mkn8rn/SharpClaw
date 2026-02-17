namespace SharpClaw.Infrastructure.Persistence.JSON;

public sealed class JsonFileOptions
{
    /// <summary>
    /// Directory where JSON data files are stored.
    /// Defaults to a "data" folder next to the application.
    /// </summary>
    public string DataDirectory { get; set; } = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!,
        "Data");
}
