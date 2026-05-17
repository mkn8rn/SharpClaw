<![CDATA[# Advanced Topics

Explore advanced SharpClaw features for power users: tool awareness, custom chat headers, environment configuration, editor bridges, and local models.

## Tool Awareness Sets

Tool awareness sets control which tool schemas are included in agent API requests. By default, all 34+ tools are enabled, adding ~2,500–3,000 tokens to every prompt. Tool awareness sets let you minimize this overhead by explicitly including or excluding tools.

### How It Works

A tool awareness set is a named entity with a dictionary of tool names → boolean values:

- **true** or **absent**: Tool is enabled
- **false**: Tool is excluded

Tool filtering follows an **override chain**:

1. **Channel** tool awareness set (highest priority)
2. **Agent** tool awareness set
3. **No set** (all tools enabled)

### Creating a Tool Awareness Set

1. Go to **Settings** → **Advanced** → **Tool Awareness**
2. Click **+ Create Set**
3. Enter a name (e.g., "Minimal Toolset")
4. For each tool, select:
   - **Include**: Tool schema is sent to the model
   - **Exclude**: Tool is hidden from the model
5. Click **Save**

### Assigning Tool Awareness Sets

**To an agent:**

1. Go to **Settings** → **Agents**
2. Click an agent
3. Select a tool awareness set from the dropdown
4. Click **Update**

**To a channel:**

1. Click a channel in the sidebar
2. Go to **Channel Settings** → **Tool Awareness**
3. Select a set
4. Click **Save**

Channel assignments override agent assignments.

### Best Practices

- **Start broad, narrow later**: Begin with all tools enabled, then create restrictive sets for specialized agents
- **Group by domain**: Create sets like "Code Assistant" (editor + shell tools), "Research Agent" (HTTP + search tools), "Support Bot" (chat + info store tools)
- **Test with different models**: Some models handle large tool sets better than others

## Custom Chat Headers

By default, every user message is prefixed with a metadata header:

```
[time: 2025-01-15 14:23:00 UTC | user: alice | via: UnoWindows | role: Developer (SafeShell, Agent) | bio: Senior engineer | agent-role: Assistant (Chat, Log)]
```

You can **override** this header at the **agent level** or **channel level** with custom templates. Operators can also turn off generated default headers globally with `Chat:DisableDefaultHeaders=true` in the Core `.env`; explicit agent or channel custom headers still run unless they are cleared or the channel's **Disable Chat Header** setting is enabled.

### Header Tag System

Custom headers support **{{tag}}** placeholders that are expanded at send-time:

**Context tags:**
- `{{time}}`: Current UTC timestamp
- `{{user}}`: Username
- `{{via}}`: Client type (CLI, API, UnoWindows, Telegram, etc.)
- `{{role}}`: User's role name
- `{{bio}}`: User's biography
- `{{agent-name}}`: Agent's name
- `{{agent-role}}`: Agent's role name
- `{{grants}}`: User's grant summary
- `{{agent-grants}}`: Agent's grant summary
- `{{editor}}`: Current editor session (if active)
- `{{accessible-threads}}`: Threads the agent can read from other channels
- `{{reasoning-effort}}`: The configured `reasoningEffort` value, rendered only for providers that accept the hint informationally (e.g. LlamaSharp, which has no mechanical reasoning-effort control). Empty string on providers that consume the value on the wire, or when no effort is configured.

**Resource tags** (list all entities of a type):
- `{{Agents}}`, `{{Models}}`, `{{Providers}}`, `{{Channels}}`, `{{Threads}}`, `{{Roles}}`, `{{Users}}`, `{{Containers}}`, `{{Websites}}`, `{{SearchEngines}}`, `{{DisplayDevices}}`, `{{EditorSessions}}`, `{{Skills}}`, `{{SystemUsers}}`, `{{LocalInfoStores}}`, `{{ExternalInfoStores}}`, `{{ScheduledTasks}}`, `{{Tasks}}`

**Per-item template:**

```
{{Agents:{Name} ({Id})}}
```

This renders as:

```
Agents: Alice (guid1), Bob (guid2), Charlie (guid3)
```

Fields marked with `[HeaderSensitive]` render as `[redacted]`.

Header expansion is on the chat hot path. `Chat:DisableAccessibleThreadsHeader=true` suppresses cross-thread summaries in the generated header and makes `{{accessible-threads}}` expand to an empty string. `Chat:DisableModuleHeaderTags=true` prevents module-owned header tag resolvers from executing inside custom headers. `Chat:RuntimeStateCacheSeconds` controls the short-lived cache used for chat contributor and header state; use `0` only when debugging and every message must force fresh state.

### Setting Custom Headers

**For an agent:**

1. Go to **Settings** → **Agents** → click agent → **Custom Header** tab
2. Enter your template (e.g., `[{{time}} | user: {{user}} | agent: {{agent-name}}]`)
3. Click **Save**

**For a channel:**

1. Click a channel → **Channel Settings** → **Custom Header** tab
2. Enter your template
3. Click **Save**

Channel headers override agent headers.

### Disabling Chat Headers

To suppress headers entirely:

- **Agent level**: Set a blank custom header
- **Channel level**: Enable **Disable Chat Header** in channel settings

For instance-wide behavior, edit Core `.env` and set `Chat:DisableDefaultHeaders=true`. To remove the core-generated native-tool instruction suffix from provider calls, set `Chat:DisableSystemPrompt=true`; this does not remove the system prompt saved on each agent.

### Permissions

Custom headers require:

- **Agent headers**: `CanEditAgentHeader` global flag OR `AgentHeaderAccesses` for specific agents
- **Channel headers**: `CanEditChannelHeader` global flag OR `ChannelHeaderAccesses` for specific channels

## Environment Configuration

SharpClaw uses two `.env` files (JSON-with-comments format):

### Core .env (API-side)

**Location:** `SharpClaw.Application.Infrastructure/Environment/.env`

**Managed via:** Settings → >env button → **Application Core** tab

**Settings:**
- `Encryption:Key`: AES encryption key for secrets
- `Jwt:Secret`: JWT signing key
- `ConnectionStrings:Postgres`: PostgreSQL connection string (optional)
- `Api:ListenUrl`: Backend bind address (default `http://127.0.0.1:48923`)
- `Admin:Username` / `Admin:Password`: First-time admin credentials
- `Browser:Executable` / `Browser:Arguments`: Chromium path for localhost browser action
- `Local:GpuLayerCount` / `Local:ContextSize` / `Local:KeepLoaded` / `Local:IdleCooldownMinutes`: Local model settings
- `Chat:DisableDefaultHeaders` / `Chat:DisableSystemPrompt` / `Chat:DisableAccessibleThreadsHeader` / `Chat:DisableModuleHeaderTags` / `Chat:RuntimeStateCacheSeconds`: Chat prompt-shaping and hot-path cache settings
- `EnvEditor:AllowNonAdmin`: Allow non-admin users to edit the Core .env
- `Backend:Enabled`: Enable/disable backend auto-start

### Interface .env (client-side)

**Location:** `SharpClaw.Uno/Environment/.env`

**Managed via:** Settings → >env button → **Application Interface** tab

**Settings:**
- `Api:Url`: Backend URL (default `http://127.0.0.1:48923`)
- `Backend:Enabled`: Enable/disable backend auto-launch from Uno client
- `Gateway:Enabled` / `Gateway:Url`: Gateway auto-launch and URL
- `Processes:Persistent`: Keep processes running on exit
- `Processes:AutoStart`: Register Windows startup scripts

### Editing .env Files

1. Click **>env** button (available on: Login, Boot, Settings sidebar footer)
2. Select **Application Core** or **Application Interface**
3. Use toggle switches or **JSON** view for raw editing
4. Click **Save & Restart** to apply changes

**Core .env** requires authorization:
- Admin users always have access
- Non-admin users require `EnvEditor:AllowNonAdmin=true`

**Interface .env** is always editable by the current user.

## Editor Bridge

The editor bridge connects IDE extensions (Visual Studio, VS Code) to SharpClaw, enabling agents to read, write, and refactor code directly in your editor.

### How It Works

1. IDE extension registers with SharpClaw via WebSocket (`ws://localhost:48923/editor/ws`)
2. Extension sends workspace context (file paths, active file, selection, language)
3. SharpClaw creates an **EditorSession** resource
4. Agents call editor action tools (Read, Write, Refactor, Execute, etc.)
5. SharpClaw routes the request to the IDE extension
6. Extension performs the action and sends results back

### Setting Up the Editor Bridge

**Visual Studio 2026:**

1. Install the `SharpClaw.VisualStudio2026` VSIX
2. Open a solution in Visual Studio
3. The extension auto-connects to SharpClaw
4. Verify in **Settings** → **Advanced** → **Editor Sessions**

**VS Code:**

1. Install the `sharpclaw-vscode` extension
2. Open a workspace
3. The extension auto-connects
4. Verify in **Settings** → **Advanced** → **Editor Sessions**

### Editor Context in Chat

When chatting from an IDE extension, the agent receives additional context:

```
[... | editor: VisualStudio 18.4.2 | workspace: C:\Projects\MyApp | file: Program.cs (C#) | selection: lines 42-56]
```

### Assigning Default Editor Sessions

Channels and contexts can specify a default editor session:

1. Go to **Channel Settings** → **Default Resources** → **Editor Session**
2. Select the session from the dropdown
3. Click **Save**

When an agent invokes an editor action without specifying a session, the default is used.

### Permissions

Editor actions require `EditorSessionAccessDB` entries on the agent's role permission set.

## Local Models

SharpClaw supports running local LLMs via the `llama.cpp` server.

### Setup

1. Download and run `llama-server` (formerly `llama.cpp`'s server binary)
2. Load a GGUF model:

```bash
llama-server -m model.gguf --host 127.0.0.1 --port 8080
```

3. In SharpClaw, create a **Custom** provider:
   - Name: "Local Llama"
   - Type: Custom
   - Base URL: `http://127.0.0.1:8080/v1`

4. Sync models from the provider
5. Create agents powered by the local model

### Configuration

Set local model parameters in Core `.env`:

- `Local:GpuLayerCount`: Number of layers offloaded to GPU (-1 = auto, 0 = CPU only)
- `Local:ContextSize`: Token context window (default 2048)
- `Local:KeepLoaded`: Keep model in memory after completion
- `Local:IdleCooldownMinutes`: Minutes before unloading idle models

## Next Steps

Continue to **Troubleshooting** for solutions to common issues.
]]>
