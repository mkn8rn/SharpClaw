namespace SharpClaw.Modules.WebAccess.Enums;

/// <summary>
/// Identifies the search engine provider, which determines the
/// API schema, authentication method, and query parameter mapping.
/// </summary>
public enum SearchEngineType
{
    /// <summary>Google Custom Search JSON API.</summary>
    Google = 0,

    /// <summary>Microsoft Bing Web Search API (Cognitive Services).</summary>
    Bing = 1,

    /// <summary>DuckDuckGo Instant Answer API (no key required).</summary>
    DuckDuckGo = 2,

    /// <summary>Brave Search API.</summary>
    Brave = 3,

    /// <summary>SearXNG federated meta-search engine (self-hosted).</summary>
    SearXNG = 4,

    /// <summary>Tavily AI-optimised search API.</summary>
    Tavily = 5,

    /// <summary>Serper.dev Google SERP scraping API.</summary>
    Serper = 6,

    /// <summary>Kagi search API.</summary>
    Kagi = 7,

    /// <summary>You.com Search API.</summary>
    YouDotCom = 8,

    /// <summary>Mojeek independent search engine API.</summary>
    Mojeek = 9,

    /// <summary>Yandex Search API.</summary>
    Yandex = 10,

    /// <summary>Baidu Search API.</summary>
    Baidu = 11,

    /// <summary>
    /// Generic custom search engine.  Accepts a query, optional
    /// page/count, and passes them as query-string parameters to
    /// the configured endpoint.
    /// </summary>
    Custom = 99,
}
