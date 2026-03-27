using SharpClaw.Gateway.Bots;
using SharpClaw.Gateway.Configuration;
using SharpClaw.Gateway.Controllers;
using SharpClaw.Gateway.Infrastructure;
using SharpClaw.Gateway.Security;

var builder = WebApplication.CreateBuilder(args);

// ── Gateway .env (same pattern as Core / Interface) ──────────────
builder.Configuration.AddGatewayEnvironment(
    isDevelopment: builder.Environment.IsDevelopment());

// ── Internal API client ──────────────────────────────────────────
builder.Services.Configure<InternalApiOptions>(
    builder.Configuration.GetSection(InternalApiOptions.SectionName));

builder.Services.AddHttpClient<InternalApiClient>(client =>
{
    var baseUrl = builder.Configuration[$"{InternalApiOptions.SectionName}:BaseUrl"]
                  ?? "http://127.0.0.1:48923";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── Gateway endpoint configuration ──────────────────────────────
builder.Services.Configure<GatewayEndpointOptions>(
    builder.Configuration.GetSection(GatewayEndpointOptions.SectionName));

// ── Bot configuration ────────────────────────────────────────────
builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.Configure<DiscordBotOptions>(
    builder.Configuration.GetSection(DiscordBotOptions.SectionName));

builder.Services.AddHttpClient("TelegramBot");
builder.Services.AddHttpClient("DiscordBot");

builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<DiscordBotService>();

// ── Security ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IpBanService>();
builder.Services.AddSharpClawRateLimiting();

// ── MVC & OpenAPI ────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "SharpClaw Gateway";
        doc.Info.Version = "v1";
        doc.Info.Description = "Public REST gateway for the SharpClaw Application API.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// ── Middleware pipeline (order matters) ──────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "SharpClaw Gateway v1");
    });
}

app.UseHttpsRedirection();

// 1. Endpoint gate — reject requests to disabled endpoint groups
app.UseMiddleware<EndpointGateMiddleware>();

// 2. IP ban check — reject banned IPs before any other processing
app.UseMiddleware<IpBanMiddleware>();

// 3. Anti-spam — body size, content-type validation
app.UseMiddleware<AntiSpamMiddleware>();

// 4. Rate limiting
app.UseRateLimiter();

// 5. WebSocket support for transcription streaming
app.UseWebSockets();

app.UseAuthorization();

app.MapControllers();
app.MapTranscriptionStreamingProxy();
app.MapChatStreamProxy();

app.Run();
