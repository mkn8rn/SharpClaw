# Eden AI

| | |
|---|---|
| **`ProviderKey`** | `eden-ai` |
| **Client class** | `EdenAIApiClient` -> `OpenAiCompatibleApiClient` |
| **Endpoint** | `https://api.edenai.run/v3` |
| **Auth** | `Authorization: Bearer {apiKey}` |
| **Protocol** | OpenAI Chat Completions (compatible) |
| **Tool calling** | Yes, native OpenAI-style tools |
| **API docs** | https://www.edenai.co/docs/api-reference/chat/chat-completions |

---

## Completion parameters

| Parameter | Supported | Range / values |
|---|---|---|
| `temperature` | Yes | `0.0` to `2.0` |
| `topP` | Yes | `0.0` to `1.0` |
| `topK` | No | - |
| `frequencyPenalty` | Yes | `-2.0` to `2.0` |
| `presencePenalty` | Yes | `-2.0` to `2.0` |
| `stop` | Yes | Up to **4** sequences |
| `seed` | Yes | Any integer |
| `responseFormat` | Yes | `json_object` and `json_schema` |
| `reasoningEffort` | Yes | `"none"`, `"disable"`, `"minimal"`, `"low"`, `"medium"`, `"high"`, `"xhigh"`, `"max"` |

---

## Wire format

Standard OpenAI Chat Completions format:

```json
{
  "model": "openai/gpt-4o",
  "messages": [
    { "role": "user", "content": "Hello" }
  ],
  "temperature": 0.7,
  "top_p": 0.9,
  "frequency_penalty": 0.5,
  "presence_penalty": 0.3,
  "stop": ["\n", "END"],
  "seed": 42,
  "response_format": { "type": "json_object" },
  "reasoning_effort": "none"
}
```

---

## Notes

Eden AI is registered as a built-in OpenAI-compatible provider by the
`sharpclaw_providers_openai_compat` module. The provider key is
`eden-ai`; do not use the old enum-style provider names when creating a
provider through the API or CLI.

Eden AI model IDs use the `provider/model` shape, for example
`openai/gpt-4o` or `anthropic/claude-sonnet-4-5`. The smart-routing model
is `@edenai`. If a model is returned by Eden AI's `/v3/models` endpoint,
`provider sync-models <providerId>` will import it automatically. If you
want to use `@edenai` and it is not returned by the listing endpoint, add
it manually as a model on the Eden AI provider.

Eden AI can route to many upstream model providers. SharpClaw validates
typed parameters against Eden AI's OpenAI-compatible gateway surface, but
the selected upstream model may still reject a parameter that the gateway
accepts. When that happens, the upstream error is returned through the
normal provider error path.

---

## `providerParameters` examples

```json
{
  "fallbacks": ["openai/gpt-4o-mini"],
  "router_candidates": ["openai/gpt-4o", "anthropic/claude-sonnet-4-5"]
}
```

-> [Back to overview](../Provider-Parameters.md)
