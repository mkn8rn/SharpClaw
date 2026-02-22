namespace Mk8.Shell.Models;

/// <summary>
/// Parses <c>mk8.shell.env</c> / <c>mk8.shell.signed.env</c> content
/// into a dictionary of environment variables. The format is simple
/// <c>KEY=VALUE</c> lines — blank lines and <c>#</c> comment lines
/// are ignored.
/// </summary>
public static class Mk8SandboxEnvParser
{
    /// <summary>
    /// Parses env content (the verified portion of a signed env file)
    /// into a case-insensitive dictionary.
    /// </summary>
    public static Dictionary<string, string> Parse(string envContent)
    {
        ArgumentNullException.ThrowIfNull(envContent);

        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in envContent.Split('\n'))
        {
            var line = rawLine.Trim();

            // Skip blank lines and comments.
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            // Strip optional surrounding quotes.
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Serializes a dictionary back into <c>KEY=VALUE</c> env format.
    /// </summary>
    public static string Serialize(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var sb = new System.Text.StringBuilder();
        foreach (var (key, value) in variables)
        {
            sb.Append(key);
            sb.Append('=');
            sb.AppendLine(value);
        }
        return sb.ToString();
    }
}
