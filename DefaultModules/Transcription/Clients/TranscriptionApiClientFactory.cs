using SharpClaw.Contracts.Enums;

namespace SharpClaw.Modules.Transcription.Clients;

public sealed class TranscriptionApiClientFactory
{
    private readonly Dictionary<ProviderType, ITranscriptionApiClient> _clients;
    private readonly ITranscriptionApiClient? _localClient;

    public TranscriptionApiClientFactory(IEnumerable<ITranscriptionApiClient> clients)
    {
        _localClient = null;
        _clients = [];
        foreach (var c in clients)
        {
            if (c.IsLocalInference)
                _localClient = c;
            else
                _clients[c.ProviderType] = c;
        }
    }

    public ITranscriptionApiClient GetClient(ProviderType providerType)
    {
        return _clients.TryGetValue(providerType, out var client)
            ? client
            : throw new NotSupportedException(
                $"Provider type '{providerType}' does not support transcription.");
    }

    public ITranscriptionApiClient GetLocalClient()
    {
        return _localClient
            ?? throw new NotSupportedException("No local transcription client is registered.");
    }

    public bool Supports(ProviderType providerType) =>
        _clients.ContainsKey(providerType);

    public bool SupportsLocal() => _localClient is not null;
}
