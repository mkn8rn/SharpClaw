using System.Text.Json;
using SharpClaw.Contracts.DTOs.Providers;
using SharpClaw.Contracts.Providers;

namespace SharpClaw.Tests.Core;

[TestFixture]
public sealed class ProviderModelCatalogEngineTests
{
    private readonly ProviderCatalogEngine _providers = new();
    private readonly ModelCatalogEngine _models = new();

    [Test]
    public void PlanCreate_WhenEndpointRequiredAndNotDiscoverable_RequiresEndpoint()
    {
        var plugin = new Plugin(
            ProviderKey: "custom",
            DisplayName: "Custom",
            RequiresEndpoint: true,
            SupportsAutomaticEndpointDiscovery: false,
            RequiresApiKey: true);

        var act = () => _providers.PlanCreate(
            new CreateProviderRequest("Custom", "custom"),
            plugin,
            enforceUniqueNames: true,
            existingProviderNames: []);

        act.Should().Throw<ArgumentException>()
            .WithMessage("ApiEndpoint is required for provider 'custom'.");
    }

    [Test]
    public void PlanCreate_WhenProviderHasNoEndpointConcept_DropsEndpoint()
    {
        var plugin = new Plugin(
            ProviderKey: "remote",
            DisplayName: "Remote",
            RequiresEndpoint: false,
            SupportsAutomaticEndpointDiscovery: false,
            RequiresApiKey: true);

        var plan = _providers.PlanCreate(
            new CreateProviderRequest(
                "Remote",
                "remote",
                ApiEndpoint: "https://ignored.example",
                ApiKey: "secret"),
            plugin,
            enforceUniqueNames: true,
            existingProviderNames: []);

        plan.ApiEndpointToStore.Should().BeNull();
        plan.ApiKey.Should().Be("secret");
    }

    [Test]
    public void PlanCreate_WhenProviderSupportsDiscovery_KeepsEndpointOverride()
    {
        var plugin = new Plugin(
            ProviderKey: "local",
            DisplayName: "Local",
            RequiresEndpoint: false,
            SupportsAutomaticEndpointDiscovery: true,
            RequiresApiKey: false);

        var plan = _providers.PlanCreate(
            new CreateProviderRequest(
                "Local",
                "local",
                ApiEndpoint: "http://localhost:11434"),
            plugin,
            enforceUniqueNames: true,
            existingProviderNames: []);

        plan.ApiEndpointToStore.Should().Be("http://localhost:11434");
    }

    [Test]
    public void PlanCreate_WhenProviderNameDuplicatesAfterTrimAndCase_Throws()
    {
        var plugin = new Plugin(
            ProviderKey: "remote",
            DisplayName: "Remote",
            RequiresEndpoint: false,
            SupportsAutomaticEndpointDiscovery: false,
            RequiresApiKey: true);

        var act = () => _providers.PlanCreate(
            new CreateProviderRequest(" OpenAI ", "remote"),
            plugin,
            enforceUniqueNames: true,
            existingProviderNames: ["openai"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A provider named ' OpenAI ' already exists.");
    }

    [Test]
    public void ListAvailableTypes_SortsAndProjectsPluginCapabilities()
    {
        var deviceCode = new DeviceCode();
        var types = _providers.ListAvailableTypes(
        [
            new Plugin("z", "Zulu", false, false, true),
            new Plugin("a", "Alpha", true, false, false, deviceCode)
        ]);

        types.Select(type => type.DisplayName).Should().Equal("Alpha", "Zulu");
        types[0].Should().Be(new ProviderTypeResponse(
            "a",
            "Alpha",
            RequiresEndpoint: true,
            SupportsAutomaticEndpointDiscovery: false,
            RequiresApiKey: false,
            SupportsDeviceCodeAuth: true));
    }

    [Test]
    public void EnsureCanSyncModels_WhenApiKeyRequiredAndMissing_Throws()
    {
        var plugin = new Plugin(
            ProviderKey: "remote",
            DisplayName: "Remote",
            RequiresEndpoint: false,
            SupportsAutomaticEndpointDiscovery: false,
            RequiresApiKey: true);

        var act = () => _providers.EnsureCanSyncModels(
            "remote",
            hasApiKey: false,
            plugin);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Provider does not have an API key configured.");
    }

    [Test]
    public void SerializeCapabilityTags_WhenEmptyOrNull_ReturnsNull()
    {
        _models.SerializeCapabilityTags(null).Should().BeNull();
        _models.SerializeCapabilityTags(Array.Empty<string>()).Should().BeNull();
    }

    [Test]
    public void SerializeCapabilityTags_WhenPresent_JoinsWithCommas()
    {
        _models.SerializeCapabilityTags(["chat", "vision"]).Should().Be("chat,vision");
    }

    [Test]
    public void UniqueNameEnforcement_DefaultsToTrueUnlessExplicitFalse()
    {
        ProviderCatalogEngine.IsUniqueNameEnforced(null).Should().BeTrue();
        ProviderCatalogEngine.IsUniqueNameEnforced("not-a-bool").Should().BeTrue();
        ProviderCatalogEngine.IsUniqueNameEnforced("true").Should().BeTrue();
        ProviderCatalogEngine.IsUniqueNameEnforced("false").Should().BeFalse();
        ModelCatalogEngine.IsUniqueNameEnforced("false").Should().BeFalse();
    }

    private sealed record Plugin(
        string ProviderKey,
        string DisplayName,
        bool RequiresEndpoint,
        bool SupportsAutomaticEndpointDiscovery,
        bool RequiresApiKey,
        IDeviceCodeFlow? DeviceCodeFlow = null) : IProviderPlugin
    {
        public IProviderApiClient CreateClient(string? endpoint) =>
            throw new NotSupportedException();

        public IModelCapabilityResolver Capabilities { get; } = new Capabilities();

        public IReadOnlyList<ProviderCostSeed> CostSeeds => [];
    }

    private sealed class Capabilities : IModelCapabilityResolver
    {
        public HashSet<string> Resolve(string modelName) => [];
    }

    private sealed class DeviceCode : IDeviceCodeFlow
    {
        public Task<DeviceCodeSession> StartAsync(
            HttpClient httpClient,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> PollAsync(
            HttpClient httpClient,
            DeviceCodeSession session,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ApiClient : IProviderApiClient
    {
        public string ProviderKey => "unused";

        public Task<IReadOnlyList<string>> ListModelIdsAsync(
            HttpClient httpClient,
            string apiKey,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ChatCompletionResult> ChatCompletionAsync(
            HttpClient httpClient,
            string apiKey,
            string model,
            string? systemPrompt,
            IReadOnlyList<ChatCompletionMessage> messages,
            int? maxCompletionTokens = null,
            Dictionary<string, JsonElement>? providerParameters = null,
            CompletionParameters? completionParameters = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
