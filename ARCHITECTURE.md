# SaasCopilot Copilot GUI — Architecture

This document covers the internal design of the SaasCopilot AI copilot subsystem. For the product overview and package guide, start with [README.md](README.md).

---

## High-Level Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  SaasCopilot Desktop Client (WinForms / Blazor hybrid)         │
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  SaasCopilot.Copilot.GUI                                 │  │
│  │                                                          │  │
│  │  Presentation ──► Application ──► Infrastructure         │  │
│  │  (WinForms UI)    (Controller)    (HTTP / MCP / SignalR) │  │
│  │                        │                                 │  │
│  │              Domain models & interfaces                  │  │
│  └──────────────────────────────────────────────────────────┘  │
│           │                    │              │                 │
│           ▼                    ▼              ▼                 │
│   LM Studio (localhost:1234)  /mcp      /copilot-hub           │
│   OpenAI-compatible REST API  (tools)   (SignalR push)         │
└────────────────────────────────────────────────────────────────┘
```

---

## Project / Package Layout

The copilot subsystem is split into four packages so that the host-side MCP service and the platform-specific UI can evolve independently:

```
SaasCopilot.Service           # Host-side MCP server contracts + tool wiring
SaasCopilot.GUI.Core          # Domain models + Infrastructure (no UI dependency)
SaasCopilot.GUI.WinForms      # WinForms presentation layer  → depends on Core
SaasCopilot.GUI.Blazor        # Blazor component layer       → depends on Core (planned)
```

### SaasCopilot.Service

Reusable MCP server class library for host applications. Targets `net10.0` and deliberately avoids direct dependencies on CargoWise, Winzor, or any other specific desktop runtime.

```
SaasCopilotServiceCollectionExtensions.cs  # AddSaasCopilotService() MCP registration
```

### SaasCopilot.GUI.Core

Shared logic with no UI dependency. Targets `net10.0` (cross-platform).

```
Application/
  McpSidecarController.cs      # Orchestrates chat loop, tool calls, approval, transcript
Domain/
  AssistantChatRequest.cs      # Immutable chat request (prompt, tools, history)
  AssistantChatResponse.cs     # Model response (message + optional tool intent)
  AssistantConversationTurn.cs # Single user/assistant turn in the history
  AssistantModelDescriptor.cs  # LLM identity (id, display name, context window)
  AssistantToolCallIntent.cs   # Parsed tool call from the model response
  AssistantToolExecution.cs    # Result of executing a tool call
  LmStudioLocalServer.cs       # Well-known LM Studio endpoint constants
  McpApprovalRequest.cs        # Data passed to the approval UI
  McpAssistantMode.cs          # Chat | Act enum
  McpConnectionState.cs        # Full connection state machine enum
  McpEndpointResolution.cs     # Success/failure result of resolving the MCP URI
  McpSidecarConfiguration.cs   # Persisted settings (panel width, endpoint, model)
  McpToolCallResult.cs         # Raw tool execution output (text + JSON)
  McpToolDescriptor.cs         # Tool metadata (name, schema, hints)
  McpToolSource.cs             # BuiltIn | Remote
  McpTranscriptEntry.cs        # Single log entry (tag + message + timestamp)
  McpTrustEntry.cs             # Persisted tool-trust record
  McpHubActionEventArgs.cs     # EventArgs for server-pushed ActionCompleted / ActionFailed
Infrastructure/
  AssistantChatService.cs      # Calls LM Studio chat/completions, handles tool follow-up
  AssistantModelSelectionService.cs  # Discovers + persists the selected LLM
  BuiltInToolExecutor.cs       # Executes file_read / file_write / run_command / search_web
  BuiltInToolProvider.cs       # Static catalogue of built-in tool descriptors
  McpApprovalPolicy.cs         # Default policy: require approval for destructive tools
  McpApprovalService.cs        # Combines policy + trust store for approval decisions
  McpConfigurationStore.cs     # JSON persistence for McpSidecarConfiguration
  McpConnectionManager.cs      # HTTP-level MCP session (initialize, send requests)
  McpDirectToolIntentParser.cs # Parses #toolName{...json...} syntax without an LLM call
  McpEndpointResolver.cs       # Derives /mcp URI from active app URI or override
  McpJsonFormatting.cs         # Shared JSON helpers
  IMcpHubConnection.cs         # Start / Stop / ActionReceived interface for the hub client
  McpHubConnection.cs          # SignalR HubConnectionBuilder implementation
  McpLogService.cs             # In-memory + transcript append service
  McpToolCatalog.cs            # Merges built-in + remote tools; pages through tools/list
  McpToolInvoker.cs            # Calls a tool (built-in or via MCP) and returns result text
  McpToolRequestTextParser.cs  # Parses tool call text embedded in plain-text model responses
  McpTranscriptStore.cs        # JSON transcript persistence (append + load-recent)
  McpTrustStore.cs             # JSON trust-list persistence
```

### SaasCopilot.GUI.WinForms

Windows-only presentation layer. Targets `net10.0-windows10.0.22621.0`.

```
Composition/
  ServiceCollectionExtensions.cs  # AddSaasCopilotCopilotGui() DI registration
  IMcpSidecarPresenter.cs
  IMcpSidecarPresenterFactory.cs
  McpSidecarPresenter.cs
  McpSidecarPresenterFactory.cs
Presentation/
  McpChatTranscriptControl.cs  # Scrollable log of all model/tool messages
  McpSidecarControl.cs         # Main chat input + response + mode selector
  McpSidecarForm.cs            # Top-level floating tool-window form
  McpSidecarSettingsForm.cs    # Endpoint override + model picker settings dialog
```

### SaasCopilot.GUI.Blazor _(planned)_

Blazor component equivalents of the WinForms controls. Targets `net10.0`.

---

## Key Concepts

### Assistant Modes

```csharp
public enum McpAssistantMode
{
    Chat,   // model answers from its own knowledge; no tools are invoked
    Act,    // model may call any tool; user can pin a specific tool with #toolName
}
```

In **Chat** mode the copilot is a conversational FAQ assistant.  
In **Act** mode the model is given the full tool catalogue and can call any tool. A user can also pin a specific tool by prefixing the message with `#toolName` (mirroring VS Code Copilot's `@agent #tool` syntax).

### MCP Integration

The copilot connects to the running SaasCopilot application via the **Model Context Protocol** (MCP — the open standard popularised by Anthropic and now adopted by most major AI tool ecosystems). The endpoint is derived automatically from the active application URI:

```
https://<SaasCopilot-host>/mcp
```

or overridden manually in settings. On connection the client sends `initialize` followed by `notifications/initialized`, then pages through `tools/list` to build the tool catalogue.

### SignalR Hub Integration

After a successful MCP connection, `McpSidecarController` also subscribes to the server's SignalR hub at `/copilot-hub` on the same host. The hub delivers real-time push events when server-side tool actions complete, without requiring the sidecar to poll.

#### Connection lifecycle

```
ConnectAsync() succeeds
  └─► StartHubAsync(ResolvedEndpoint)
        └─► McpHubConnection.StartAsync(https://<host>/copilot-hub)
              ├─ subscribes "ActionCompleted" → OnHubActionReceived
              └─ subscribes "ActionFailed"    → OnHubActionReceived

SetVisible(false)  OR  endpoint changes
  └─► McpHubConnection.StopAsync()
```

#### Event flow

```
Server (CargoWise.Winzor.AppServer)
  └─ IHubContext<CopilotHub>.SendAsync("ActionCompleted", McpHubActionEventArgs)
       │  WebSocket push
       ▼
McpHubConnection.On<McpHubActionEventArgs>("ActionCompleted")
  └─► ActionReceived event
        └─► McpSidecarController.OnHubActionReceived
              └─ posts to WinForms SynchronizationContext
                   └─ AppendTranscript("hub", ...)  +  NotifyStateChanged()
                        └─ transcript shows "Tool 'open_module' → 'AR' succeeded."
```

#### Key types

| Type | Location | Role |
|---|---|---|
| `McpHubActionEventArgs` | `Domain/` | Event data: `Tool`, `Key`, `Success` |
| `IMcpHubConnection` | `Infrastructure/` | `StartAsync` / `StopAsync` / `ActionReceived` event |
| `McpHubConnection` | `Infrastructure/` | `HubConnectionBuilder` with `WithAutomaticReconnect()` |

The hub subscription is **additive and non-blocking**: failure to connect to `/copilot-hub` is logged as a transcript warning and does not affect MCP tool calling in any way.

### How to Expose New MCP Tools from the Application Host

The sidecar discovers tools automatically from whatever the `/mcp` server exposes. The steps below apply to any host that wants to register new tools the AI model can call.

#### 1. Define the tool on the server (CargoWise / SaasCopilot AppServer)

```csharp
// Anywhere in your host assembly — the [McpServerTool] attribute registers it.
[McpServerTool]
[Description("Opens a named module in the current session.")]
public async Task<string> OpenModule(
    [Description("Stable module key returned by list_modules.")] string moduleKey,
    CancellationToken ct)
{
    var result = await _moduleLaunchService.LaunchAsync(moduleKey, _hubContext, ct);
    return JsonSerializer.Serialize(result);
}
```

The `inputSchema` (a JSON Schema object) is generated automatically from the parameter types and `[Description]` attributes. The AI model reads it at session start via `tools/list` and uses it to reason about which arguments to supply.

#### 2. Register the MCP server and map the endpoint

```csharp
// Program.cs / Startup.cs in the host
builder.Services.AddMcpServer()
    .WithTools<YourToolClass>();

app.MapMcp("/mcp");          // sidecar connects here
app.MapHub<CopilotHub>("/copilot-hub");  // hub push channel
```

#### 3. Push real-time feedback from the hub after dispatch

After the tool's dispatcher action resolves, fire a hub push (fire-and-forget) so the transcript shows the outcome immediately:

```csharp
_ = hubContext.Clients.All.SendAsync(
    "ActionCompleted",
    new McpHubActionEventArgs { Tool = "open_module", Key = moduleKey, Success = true },
    CancellationToken.None);
```

The sidecar's `OnHubActionReceived` will append a `"hub"` transcript entry automatically.

#### 4. What the sidecar does with it

No sidecar code changes are required when new tools are added to the server. The sidecar:

1. Calls `tools/list` on `ConnectAsync` and adds every discovered tool to its catalogue.
2. Sends the relevant `inputSchema` to the model as part of each prompt request.
3. Parses the model's `tool_choice` / `tool_calls` response and calls `tools/call` via `McpConnectionManager`.
4. Receives the tool result as a JSON string and feeds it back to the model for a final response.
5. Appends any hub-pushed `ActionCompleted` / `ActionFailed` events to the transcript in real time.

#### Naming conventions for tool parameters

| Convention | Example | Why |
|---|---|---|
| Use `[Description]` on every parameter | `[Description("Stable module key.")]` | Populates `inputSchema.properties[x].description`; the model uses it to pick the right value |
| Prefer `string` over `int` for identifier parameters | `string moduleKey` | Models hallucinate fewer errors on string parameters |
| Return a JSON-serialised result object | `JsonSerializer.Serialize(result)` | Lets the model parse success/failure fields and decide whether to retry |

### Built-in Tools

Four tools are always available regardless of whether an MCP server is connected:

| Tool | Read/Write | Description |
|---|---|---|
| `file_read` | read-only | Read a local file by absolute or relative path |
| `file_write` | **destructive** | Create or overwrite a local file |
| `run_command` | **destructive** | Run a shell command and capture stdout/stderr |
| `search_web` | read-only | Fetch the text content of an HTTP/HTTPS URL |

Remote tools are discovered from the SaasCopilot `/mcp` endpoint at connection time and can be anything the server exposes (e.g. shipment lookup, customs entry query, document generation).

### Tool Approval & Trust

Before any **destructive** or **mutating** tool is executed the approval service checks two things:

1. **Policy** (`IMcpApprovalPolicy`) — does this tool category require a prompt?
2. **Trust store** (`McpTrustStore`) — has the user previously approved this tool for this endpoint?

If approval is required a dialog is shown. The user can approve once or approve-and-remember (persisted to `trust.json` on disk). Every decision is written to the transcript log.

### Model Backend

The copilot targets [LM Studio](https://lmstudio.ai/) running on `http://localhost:1234/v1/`. Any model loaded in LM Studio that supports the OpenAI Chat Completions API with `tools` / `tool_choice` is compatible (e.g. Qwen2.5-Coder, Mistral, Llama 3, Phi-4).

Model discovery uses LM Studio's `/api/v0/models` endpoint (which returns richer metadata such as `context_length`) and falls back to the OpenAI-compatible `/v1/models`. The selected model ID is persisted in the configuration store.

---

## Roadmap

Items are roughly ordered from near-term to longer-term.

### Near-term

- [x] **Streaming responses** — switch from a single HTTP response to SSE / chunked streaming so the user sees tokens as they are generated.
- [ ] **Conversation context pruning** — automatically drop the oldest turns when the conversation exceeds the model's context window instead of silently retrying without tools.
- [ ] **Approval UI polish** — show the tool's title and a formatted preview of the arguments in the approval dialog (today only the tool name is shown).
- [ ] **Transcript pagination** — the transcript control currently loads the most-recent 50 entries on startup; add a "load earlier" button for long sessions.
- [ ] **Act mode per-tool confirmation toggle** — let users mark individual tools as "always trust" directly from the chat UI, without going into settings.

### Medium-term

- [ ] **Cloud model support** — add an optional OpenAI / Azure OpenAI / Anthropic backend so teams without a capable local GPU can still use the copilot, with the user's API key stored in Windows Credential Manager.
- [ ] **Slash-command shortcuts** — expose common SaasCopilot operations (e.g. `/shipment <id>`, `/customs-entry <ref>`) as first-class commands that skip the LLM routing step entirely.
- [ ] **Inline suggestions** — surface single-field autocomplete suggestions at the cursor position inside SaasCopilot data-entry forms, similar to GitHub Copilot's inline completion.
- [ ] **Conversation branching** — allow the user to fork a conversation at any turn and explore an alternate tool-call sequence, keeping both branches in the transcript.
- [ ] **MCP server discovery** — auto-discover multiple MCP servers (e.g. separate servers for customs, freight, and finance modules) and present tools grouped by server.

### Longer-term

- [ ] **Agent workflows** — let the model chain more than `MaxToolCallsPerTurn` (currently 2) steps to complete multi-stage SaasCopilot workflows autonomously (e.g. create a shipment, attach documents, submit a customs entry).
- [ ] **Role-based tool visibility** — filter the tool catalogue based on the logged-in SaasCopilot user's permissions so the model never offers a tool the user is not authorised to execute.
- [ ] **Telemetry & feedback** — opt-in usage analytics (what tools were called, how often approvals were granted/denied) to drive product decisions, with a thumbs-up/down reaction on each response.
- [ ] **Plugin API** — publish a stable `IBuiltInToolProvider` extension point so SaasCopilot modules can register their own tools without modifying this project.
- [ ] **Mac / Linux support** — decouple the WinForms presentation layer behind an abstraction so the copilot logic can eventually be surfaced in a cross-platform shell (e.g. MAUI or Avalonia).