namespace SharpClaw.Contracts.Models;

/// <summary>
/// Well-known capability tag keys used to describe model capabilities.
/// These replace the former <c>ModelCapability</c> flags enum and are
/// stored as comma-separated entries in <c>ModelDB.CapabilityTagsRaw</c>.
/// </summary>
public static class WellKnownCapabilityKeys
{
    /// <summary>The model supports chat completions.</summary>
    public const string Chat = "chat";

    /// <summary>The model supports vision (image) inputs.</summary>
    public const string Vision = "vision";

    /// <summary>The model produces embeddings.</summary>
    public const string Embedding = "embedding";

    /// <summary>The model produces speech audio (text-to-speech).</summary>
    public const string Tts = "tts";

    /// <summary>The model generates images.</summary>
    public const string ImageGeneration = "image-generation";
}
