namespace SharpClaw.Application.Core.LocalInference;

/// <summary>
/// Downloads model files to the local models directory with progress
/// tracking and HTTP Range resume support.
/// </summary>
public sealed class ModelDownloadManager(IHttpClientFactory httpClientFactory)
{
    private static readonly string ModelsDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SharpClaw", "models");

    public string GetModelPath(string filename) =>
        Path.Combine(ModelsDirectory, filename);

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromHours(12);

        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Resume support
        var existingLength = File.Exists(destinationPath)
            ? new FileInfo(destinationPath).Length : 0L;
        if (existingLength > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength.GetValueOrDefault() + existingLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            destinationPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        var totalRead = existingLength;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            if (totalBytes > 0)
                progress?.Report((double)totalRead / totalBytes);
        }
    }
}
