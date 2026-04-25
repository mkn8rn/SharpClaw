namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Well-known string keys that identify the client interface that originated
/// a chat message.  Stored as-is in <c>ChatMessageDB.ClientType</c> and
/// surfaced in the chat header <c>via:</c> field.
/// </summary>
public static class WellKnownClientKeys
{
    public const string Cli         = "CLI";
    public const string Api         = "API";
    public const string UnoWindows  = "UnoWindows";
    public const string UnoAndroid  = "UnoAndroid";
    public const string UnoMacOS    = "UnoMacOS";
    public const string UnoLinux    = "UnoLinux";
    public const string UnoBrowser  = "UnoBrowser";
    public const string Other       = "Other";
}
