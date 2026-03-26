using System.Text.Json;

namespace SharpClaw.Application.Core.Clients;

/// <summary>
/// Translates native Google Gemini / Vertex AI provider parameters to
/// their OpenAI-compatible equivalents so they work through the
/// <c>/openai</c> compatibility endpoints.
/// </summary>
internal static class GoogleParameterTranslator
{
    /// <summary>
    /// Translates known Gemini-native parameters to OpenAI-compatible form.
    /// <list type="bullet">
    ///   <item>
    ///     <c>generation_config: { ... }</c> — unwrapped: inner keys are
    ///     promoted to the top level (existing top-level keys take precedence).
    ///   </item>
    ///   <item>
    ///     <c>response_mime_type: "application/json"</c> →
    ///     <c>response_format: { "type": "json_object" }</c>
    ///   </item>
    ///   <item>
    ///     <c>response_mime_type: "text/plain"</c> → removed (text is the default).
    ///   </item>
    /// </list>
    /// </summary>
    internal static Dictionary<string, JsonElement>? Translate(
        Dictionary<string, JsonElement>? providerParameters)
    {
        if (providerParameters is null || providerParameters.Count == 0)
            return providerParameters;

        var needsUnwrap = providerParameters.ContainsKey("generation_config");
        var needsMimeTranslation = providerParameters.ContainsKey("response_mime_type");

        if (!needsUnwrap && !needsMimeTranslation)
            return providerParameters;

        var translated = new Dictionary<string, JsonElement>(providerParameters);

        // Phase 1: Unwrap generation_config contents to top level.
        if (needsUnwrap &&
            translated.Remove("generation_config", out var configElement) &&
            configElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in configElement.EnumerateObject())
            {
                // Top-level keys set directly by the user take precedence.
                translated.TryAdd(prop.Name, prop.Value.Clone());
            }
        }

        // Phase 2: Translate response_mime_type → response_format.
        // This covers both direct top-level usage and values unwrapped
        // from generation_config in phase 1.
        if (translated.Remove("response_mime_type", out var mimeElement) &&
            mimeElement.ValueKind == JsonValueKind.String &&
            mimeElement.GetString() is "application/json" &&
            !translated.ContainsKey("response_format"))
        {
            translated["response_format"] =
                JsonSerializer.SerializeToElement(new { type = "json_object" });
        }

        return translated;
    }
}
