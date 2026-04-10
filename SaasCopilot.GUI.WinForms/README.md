# SaasCopilot.GUI.WinForms

A Windows Forms AI copilot panel for the SaasCopilot desktop client. Backed by a locally-running LLM via [LM Studio](https://lmstudio.ai/), it can call **MCP tools** exposed by a live SaasCopilot application — keeping all data fully on-premise.

> For the repository overview and package map see [README.md](../../README.md).
>
> For architecture details, design decisions, and roadmap see [ARCHITECTURE.md](../../ARCHITECTURE.md).

---

## Requirements

- Windows 10 (build 17763) or later
- .NET 10 (Windows)
- [LM Studio](https://lmstudio.ai/) running on `http://localhost:1234`
- A SaasCopilot application instance exposing a `/mcp` endpoint _(Act mode only)_

---

## Installation

```
dotnet add package SaasCopilot.GUI.WinForms
```

---

## Quick Start

### 1. Register services

```csharp
var services = new ServiceCollection();
services.AddSaasCopilotCopilotGui();
var provider = services.BuildServiceProvider();
```

### 2. Show the copilot panel

```csharp
var factory = provider.GetRequiredService<IMcpSidecarPresenterFactory>();
using var presenter = factory.Create(new Uri("https://my-saascopilot-host"));
presenter.Show();
```

`IMcpSidecarPresenterFactory.Create` derives the `/mcp` endpoint from the URI automatically. Call `presenter.Hide()` / `presenter.Dispose()` to close the panel.

---

## Configuration

Settings are persisted to `%APPDATA%\SaasCopilot\mcp-config.json` via `IMcpConfigurationStore`.

| Property | Default | Description |
|---|---|---|
| `McpEndpointOverride` | `null` | Override the auto-derived `/mcp` URI |
| `SelectedModelId` | `null` | LM Studio model ID; `null` = first available |
| `PanelWidth` | `400` | Width of the floating panel in pixels |

---

## Public API

### DI extension

```csharp
// registers all services needed by the copilot panel
services.AddSaasCopilotCopilotGui();
```

### Presenter (recommended entry point)

```csharp
IMcpSidecarPresenterFactory   // resolve from DI
    .Create(Uri appBaseUri) → IMcpSidecarPresenter

IMcpSidecarPresenter
    .Show()
    .Hide()
    .Dispose()
```

### WinForms controls (advanced / custom embedding)

| Class | Description |
|---|---|
| `McpSidecarForm` | Top-level floating tool window |
| `McpSidecarControl` | Chat input + response area + mode selector |
| `McpChatTranscriptControl` | Scrollable log of all tool/model messages |
| `McpSidecarSettingsForm` | Endpoint override + model picker dialog |

### Assistant modes

```csharp
McpAssistantMode.Chat  // LLM answers from its own knowledge; no tools called
McpAssistantMode.Act   // LLM may call any MCP tool; prefix with #toolName to pin one
```

---

## Built-in Tools

Always available regardless of MCP connection:

| Tool | Description |
|---|---|
| `file_read` | Read a local file |
| `file_write` | Create or overwrite a local file |
| `run_command` | Run a shell command and capture stdout/stderr |
| `search_web` | Fetch text content from an HTTP/HTTPS URL |

Destructive tools (`file_write`, `run_command`) require explicit user approval before execution.

---

## See Also

- [README.md](../../README.md) — repository overview and package map
- [ARCHITECTURE.md](../../ARCHITECTURE.md) — architecture, key concepts, project structure, roadmap
- [LM Studio](https://lmstudio.ai/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
