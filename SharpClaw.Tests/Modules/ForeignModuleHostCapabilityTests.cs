using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Application.Core.Modules.Foreign;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Tests.Modules;

[TestFixture]
public sealed class ForeignModuleHostCapabilityTests
{
    [Test]
    public async Task HostCapabilityServerRejectsCallsWithoutPerRunToken()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var server = ForeignModuleHostCapabilityServer.Start("sample_module", services);
        using var client = new HttpClient { BaseAddress = server.Address };

        using var response = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.LogPath,
            new { message = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task HostCapabilityServerForwardsJobLifecycleCalls()
    {
        var jobs = new RecordingJobController();
        await using var services = new ServiceCollection()
            .AddSingleton<IAgentJobController>(jobs)
            .BuildServiceProvider();
        await using var server = ForeignModuleHostCapabilityServer.Start("sample_module", services);
        using var client = CreateClient(server);
        var jobId = Guid.NewGuid();

        using var logResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.JobLogPath,
            new { jobId, message = "step one", level = "Warning" });
        using var completeResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.JobCompletePath,
            new { jobId, resultData = """{"ok":true}""", message = "done" });
        using var failResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.JobFailPath,
            new { jobId, message = "failed", details = "details" });
        using var cancelResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.JobCancelPath,
            new { jobId, message = "stopping" });

        logResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        failResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        jobs.Logs.Should().ContainSingle()
            .Which.Should().Be((jobId, "step one", "Warning"));
        jobs.Completed.Should().ContainSingle()
            .Which.Should().Be((jobId, """{"ok":true}""", "done"));
        jobs.Failed.Should().ContainSingle()
            .Which.Should().Be((jobId, "failed", "details"));
        jobs.Cancelled.Should().ContainSingle()
            .Which.Should().Be((jobId, "stopping"));
    }

    [Test]
    public async Task HostCapabilityServerUsesProvidedConfigStore()
    {
        var config = new RecordingConfigStore();
        await using var services = new ServiceCollection()
            .AddSingleton<IModuleConfigStore>(config)
            .BuildServiceProvider();
        await using var server = ForeignModuleHostCapabilityServer.Start("sample_module", services);
        using var client = CreateClient(server);

        using var setResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.ConfigSetPath,
            new { key = "theme", value = "dense" });
        using var getResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.ConfigGetPath,
            new { key = "theme" });
        var getPayload = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());

        setResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getPayload.RootElement.GetProperty("value").GetString().Should().Be("dense");
    }

    [Test]
    public async Task HostCapabilityServerInvokesProtocolContracts()
    {
        var resolver = new RecordingProtocolContractResolver();
        await using var services = new ServiceCollection()
            .AddSingleton<IForeignModuleProtocolContractResolver>(resolver)
            .BuildServiceProvider();
        await using var server = ForeignModuleHostCapabilityServer.Start("sample_module", services);
        using var client = CreateClient(server);

        using var listResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.ProtocolContractsListPath,
            new { });
        using var invokeResponse = await client.PostAsJsonAsync(
            ForeignModuleHostCapabilityProtocol.ProtocolContractInvokePath,
            new
            {
                contractName = "editor_bridge",
                operation = "open_file",
                parameters = new
                {
                    path = "README.md",
                },
            });
        var listPayload = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var invokePayload = JsonDocument.Parse(await invokeResponse.Content.ReadAsStringAsync());

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        invokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listPayload.RootElement.GetProperty("contracts")[0]
            .GetProperty("contractName").GetString().Should().Be("editor_bridge");
        invokePayload.RootElement.GetProperty("result")
            .GetProperty("operation").GetString().Should().Be("open_file");
        resolver.Invoker.LastParameters.GetProperty("path").GetString().Should().Be("README.md");
    }

    private static HttpClient CreateClient(ForeignModuleHostCapabilityServer server)
    {
        var client = new HttpClient { BaseAddress = server.Address };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            ForeignModuleProtocol.TokenHeaderName,
            server.Token);
        return client;
    }

    private sealed class RecordingConfigStore : IModuleConfigStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            where T : IParsable<T>
        {
            var value = _values.GetValueOrDefault(key);
            return Task.FromResult(value is not null
                && T.TryParse(value, null, out var parsed)
                    ? parsed
                    : default);
        }

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            if (value is null)
                _values.Remove(key);
            else
                _values[key] = value;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class RecordingJobController : IAgentJobController
    {
        public List<(Guid JobId, string Message, string Level)> Logs { get; } = [];
        public List<(Guid JobId, string? ResultData, string? Message)> Completed { get; } = [];
        public List<(Guid JobId, string Message, string? Details)> Failed { get; } = [];
        public List<(Guid JobId, string? Message)> Cancelled { get; } = [];

        public Task<AgentJobResponse> SubmitJobAsync(
            Guid channelId,
            SubmitAgentJobRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AgentJobResponse?> StopJobAsync(
            Guid jobId,
            string? requiredActionPrefix = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddJobLogAsync(
            Guid jobId,
            string message,
            string level = "Info",
            CancellationToken ct = default)
        {
            Logs.Add((jobId, message, level));
            return Task.CompletedTask;
        }

        public Task MarkJobCompletedAsync(
            Guid jobId,
            string? resultData = null,
            string? message = null,
            CancellationToken ct = default)
        {
            Completed.Add((jobId, resultData, message));
            return Task.CompletedTask;
        }

        public Task MarkJobFailedAsync(
            Guid jobId,
            Exception exception,
            CancellationToken ct = default)
        {
            Failed.Add((jobId, exception.Message, exception.ToString()));
            return Task.CompletedTask;
        }

        public Task MarkJobFailedAsync(
            Guid jobId,
            string message,
            string? details = null,
            CancellationToken ct = default)
        {
            Failed.Add((jobId, message, details));
            return Task.CompletedTask;
        }

        public Task MarkJobCancelledAsync(
            Guid jobId,
            string? message = null,
            CancellationToken ct = default)
        {
            Cancelled.Add((jobId, message));
            return Task.CompletedTask;
        }

        public Task CancelStaleJobsByActionPrefixAsync(
            string actionKeyPrefix,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProtocolContractResolver : IForeignModuleProtocolContractResolver
    {
        private readonly ForeignModuleProtocolContractExport _export = CreateExport();

        public RecordingProtocolContractInvoker Invoker { get; } = new();

        public IForeignModuleProtocolContractInvoker? Resolve(string contractName) =>
            contractName == _export.ContractName ? Invoker : null;

        public IReadOnlyList<ForeignModuleProtocolContractExport> GetAllExports() => [_export];

        private static ForeignModuleProtocolContractExport CreateExport() =>
            new(
                "editor_bridge",
                EmptyObjectSchema(),
                [
                    new ForeignModuleProtocolContractOperation(
                        "open_file",
                        EmptyObjectSchema(),
                        EmptyObjectSchema())
                ]);
    }

    private sealed class RecordingProtocolContractInvoker : IForeignModuleProtocolContractInvoker
    {
        public string ContractName => "editor_bridge";
        public IReadOnlyList<ForeignModuleProtocolContractOperation> Operations => [];
        public JsonElement LastParameters { get; private set; }

        public Task<JsonElement> InvokeAsync(
            string operation,
            JsonElement parameters,
            CancellationToken ct = default)
        {
            LastParameters = parameters.Clone();
            using var document = JsonDocument.Parse(
                $$"""{"operation":"{{operation}}","ok":true}""");
            return Task.FromResult(document.RootElement.Clone());
        }
    }

    private static JsonElement EmptyObjectSchema()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return document.RootElement.Clone();
    }
}
