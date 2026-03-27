namespace SharpClaw.Gateway.Configuration;

/// <summary>
/// Per-endpoint enable/disable toggles for the public gateway.
/// Loaded from the <c>Gateway:Endpoints</c> configuration section.
/// When <see cref="Enabled"/> is <c>false</c>, the entire gateway
/// returns <c>503 Service Unavailable</c> for every request.
/// Individual endpoint groups can be toggled independently.
/// </summary>
public sealed class GatewayEndpointOptions
{
    public const string SectionName = "Gateway:Endpoints";

    /// <summary>Master kill-switch for the entire public gateway.</summary>
    public bool Enabled { get; set; } = true;

    public bool Auth { get; set; } = true;
    public bool Agents { get; set; } = true;
    public bool Channels { get; set; } = true;
    public bool ChannelContexts { get; set; } = true;
    public bool Chat { get; set; } = true;
    public bool ChatStream { get; set; } = true;
    public bool Threads { get; set; } = true;
    public bool ThreadChat { get; set; } = true;
    public bool Jobs { get; set; } = true;
    public bool Models { get; set; } = true;
    public bool Providers { get; set; } = true;
    public bool Roles { get; set; } = true;
    public bool Users { get; set; } = true;
    public bool AudioDevices { get; set; } = true;
    public bool Transcription { get; set; } = true;
    public bool TranscriptionStreaming { get; set; } = true;
    public bool Cost { get; set; } = true;
    public bool Bots { get; set; } = true;

    /// <summary>
    /// Resolves whether a given endpoint group is active.
    /// Returns <c>false</c> if the master switch is off or the
    /// specific group is disabled.
    /// </summary>
    public bool IsEndpointEnabled(string groupName)
    {
        if (!Enabled) return false;

        return groupName switch
        {
            nameof(Auth) => Auth,
            nameof(Agents) => Agents,
            nameof(Channels) => Channels,
            nameof(ChannelContexts) => ChannelContexts,
            nameof(Chat) => Chat,
            nameof(ChatStream) => ChatStream,
            nameof(Threads) => Threads,
            nameof(ThreadChat) => ThreadChat,
            nameof(Jobs) => Jobs,
            nameof(Models) => Models,
            nameof(Providers) => Providers,
            nameof(Roles) => Roles,
            nameof(Users) => Users,
            nameof(AudioDevices) => AudioDevices,
            nameof(Transcription) => Transcription,
            nameof(TranscriptionStreaming) => TranscriptionStreaming,
            nameof(Cost) => Cost,
            nameof(Bots) => Bots,
            _ => true
        };
    }
}
