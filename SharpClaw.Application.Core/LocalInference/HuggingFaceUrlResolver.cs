using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SharpClaw.Application.Core.LocalInference;

/// <summary>
/// Resolves HuggingFace model URLs to direct GGUF download links.
/// Supports:
///   - Direct file links: https://huggingface.co/{org}/{repo}/resolve/main/{file}.gguf
///   - Repo links: https://huggingface.co/{org}/{repo} → lists GGUF files via API
///   - Any direct URL to a .gguf file
/// </summary>
public sealed partial class HuggingFaceUrlResolver(IHttpClientFactory httpClientFactory)
{
    private const string HfApiBase = "https://huggingface.co/api/models";

    public async Task<IReadOnlyList<ResolvedModelFile>> ResolveAsync(
        string url, CancellationToken ct = default)
    {
        if (!url.Contains("huggingface.co", StringComparison.OrdinalIgnoreCase))
            return [new ResolvedModelFile(url, Path.GetFileName(new Uri(url).AbsolutePath), null)];

        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Trim('/').Split('/');

        // Direct file link: /org/repo/resolve/main/file.gguf
        if (segments.Length >= 5 && segments[2] == "resolve")
        {
            var filename = segments[^1];
            var quant = ParseQuantization(filename);
            return [new ResolvedModelFile(url, filename, quant)];
        }

        // Repo link: /org/repo → query API for GGUF siblings
        if (segments.Length >= 2)
        {
            var repoId = $"{segments[0]}/{segments[1]}";
            return await ListGgufFilesAsync(repoId, ct);
        }

        return [new ResolvedModelFile(url, "model.gguf", null)];
    }

    private async Task<IReadOnlyList<ResolvedModelFile>> ListGgufFilesAsync(
        string repoId, CancellationToken ct)
    {
        using var http = httpClientFactory.CreateClient();
        var apiUrl = $"{HfApiBase}/{repoId}";
        var response = await http.GetFromJsonAsync<HfModelInfo>(apiUrl, ct);

        return response?.Siblings?
            .Where(s => s.Rfilename?.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) == true)
            .Select(s => new ResolvedModelFile(
                $"https://huggingface.co/{repoId}/resolve/main/{s.Rfilename}",
                s.Rfilename!,
                ParseQuantization(s.Rfilename!)))
            .ToList() ?? [];
    }

    private static string? ParseQuantization(string filename)
    {
        var match = QuantizationPattern().Match(filename);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"[IQ]\d[_\w]+", RegexOptions.IgnoreCase)]
    private static partial Regex QuantizationPattern();

    private sealed record HfModelInfo(
        [property: JsonPropertyName("siblings")] List<HfSibling>? Siblings);

    private sealed record HfSibling(
        [property: JsonPropertyName("rfilename")] string? Rfilename);
}

public sealed record ResolvedModelFile(string DownloadUrl, string Filename, string? Quantization);
