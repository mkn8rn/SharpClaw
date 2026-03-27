using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpClaw.Gateway.Configuration;

namespace SharpClaw.Gateway.Bots;

/// <summary>
/// Hosted service that runs the Telegram bot using long-polling against
/// the Telegram Bot API. Receives incoming messages and will eventually
/// forward them to the SharpClaw core via <c>InternalApiClient</c>.
/// <para>
/// Requires a valid <see cref="TelegramBotOptions.BotToken"/>.
/// The actual message-to-core relay is not yet implemented — this
/// scaffold handles connection lifecycle, update polling, and logging.
/// </para>
/// </summary>
public sealed class TelegramBotService : BackgroundService
{
    private readonly IOptions<TelegramBotOptions> _options;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private const string TelegramApiBase = "https://api.telegram.org/bot";

    public TelegramBotService(
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramBotService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;

        if (!opts.Enabled)
        {
            _logger.LogInformation("Telegram bot is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.BotToken))
        {
            _logger.LogWarning("Telegram bot is enabled but no BotToken is configured. Stopping.");
            return;
        }

        _logger.LogInformation("Telegram bot starting with long-polling...");

        var client = _httpClientFactory.CreateClient("TelegramBot");
        client.BaseAddress = new Uri($"{TelegramApiBase}{opts.BotToken}/");
        client.Timeout = TimeSpan.FromSeconds(35);

        // Validate token on startup
        if (!await ValidateBotTokenAsync(client, stoppingToken))
        {
            _logger.LogError("Telegram bot token validation failed. Stopping.");
            return;
        }

        long offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await PollUpdatesAsync(client, offset, stoppingToken);
                if (updates is not null)
                {
                    foreach (var update in updates)
                    {
                        offset = update.UpdateId + 1;
                        await HandleUpdateAsync(client, update, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram polling error. Retrying in 5 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Telegram bot stopped.");
    }

    private async Task<bool> ValidateBotTokenAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync("getMe", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Telegram getMe returned {Status}.", response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Telegram bot authenticated: {Response}", json);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate Telegram bot token.");
            return false;
        }
    }

    private async Task<TelegramUpdate[]?> PollUpdatesAsync(
        HttpClient client, long offset, CancellationToken ct)
    {
        var url = $"getUpdates?offset={offset}&timeout=30&allowed_updates=[\"message\"]";
        var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram getUpdates returned {Status}.", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<TelegramApiResponse<TelegramUpdate[]>>(json, JsonOptions);
        return result is { Ok: true } ? result.Result : null;
    }

    private async Task HandleUpdateAsync(
        HttpClient client, TelegramUpdate update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } text } message)
            return;

        var chatId = message.Chat.Id;
        var from = message.From?.Username ?? message.From?.FirstName ?? "unknown";

        _logger.LogInformation(
            "Telegram message from @{User} in chat {ChatId}: {Text}",
            from, chatId, text);

        // TODO: Forward to SharpClaw core via InternalApiClient and return the response.
        // For now, acknowledge with a placeholder.
        await SendMessageAsync(client, chatId,
            "⚙️ SharpClaw gateway received your message. Core relay is not yet implemented.", ct);
    }

    private async Task SendMessageAsync(
        HttpClient client, long chatId, string text, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { chat_id = chatId, text }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            await client.PostAsync("sendMessage", content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram message to chat {ChatId}.", chatId);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    // ── Minimal Telegram API models ──────────────────────────────

    private sealed record TelegramApiResponse<T>
    {
        public bool Ok { get; init; }
        public T? Result { get; init; }
    }

    private sealed record TelegramUpdate
    {
        public long UpdateId { get; init; }
        public TelegramMessage? Message { get; init; }
    }

    private sealed record TelegramMessage
    {
        public long MessageId { get; init; }
        public TelegramChat Chat { get; init; } = default!;
        public TelegramUser? From { get; init; }
        public string? Text { get; init; }
    }

    private sealed record TelegramChat
    {
        public long Id { get; init; }
        public string? Title { get; init; }
        public string? Type { get; init; }
    }

    private sealed record TelegramUser
    {
        public long Id { get; init; }
        public string? FirstName { get; init; }
        public string? Username { get; init; }
    }
}
