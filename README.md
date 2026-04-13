# SaasCopilot Copilot GUI

An on-premise AI copilot for the SaasCopilot desktop client.

The copilot embeds a chat-style assistant directly inside SaasCopilot. A locally hosted language model, served through LM Studio, can answer questions, inspect conversation context, and call tools exposed by the running SaasCopilot application through the Model Context Protocol (MCP).

Unlike editor-centric copilots, this project is built around live SaasCopilot workflows rather than source code. The model can stay in plain chat mode, or switch to act mode and invoke domain-specific tools against the user's current SaasCopilot session.

## Capabilities

- Answer SaasCopilot how-to questions from the model's own knowledge in chat mode.
- Query live shipment, customs, and logistics data through MCP tools in act mode.
- Execute built-in local tools for file reads, file writes, shell commands, and HTTP fetches.
- Stream model output and tool activity into a transcript so users can see what happened.
- Keep data on-premise by using a local LLM backend instead of a hosted AI service.

## How It Fits

| Area | SaasCopilot Copilot GUI |
|---|---|
| Host application | SaasCopilot desktop client |
| Tool protocol | MCP |
| Model backend | Local LLM through LM Studio |
| Domain context | Live SaasCopilot session data |
| Data residency | On-premise |
| Interaction model | Chat plus tool-calling assistant |

## Package Layout

The subsystem is split so the UI layer can evolve independently from the shared assistant logic.

| Package | Purpose |
|---|---|
| `SaasCopilot.Service` | Host-side MCP server class library with reusable tool contracts and host-implemented service interfaces |
| `SaasCopilot.GUI.Core` | Domain models, application orchestration, MCP integration, approval flow, transcripts, and LM Studio communication |
| `SaasCopilot.GUI.WinForms` | Shipping Windows desktop UI for the copilot panel |
| `SaasCopilot.GUI.Blazor` | Planned Blazor component layer built on top of the same core services |

## Runtime Requirements

- .NET 10 SDK/runtime
- LM Studio running locally on `http://localhost:1234`
- A SaasCopilot application instance exposing `/mcp` for act mode tool calling
- A SaasCopilot application instance exposing `/copilot-hub` (SignalR) for real-time action feedback _(optional — MCP still works without it)_
- Windows for the WinForms UI package

## Documentation Map

- [ARCHITECTURE.md](ARCHITECTURE.md) for internal design, data flow, core services, and roadmap
- [src/SaasCopilot.GUI.Core/README.md](src/SaasCopilot.GUI.Core/README.md) for the platform-agnostic core package
- [src/SaasCopilot.GUI.WinForms/README.md](src/SaasCopilot.GUI.WinForms/README.md) for the WinForms package and usage entry points
- [src/SaasCopilot.GUI.Blazor/README.md](src/SaasCopilot.GUI.Blazor/README.md) for the planned Blazor package

## Current Status

The shared core and WinForms UI are implemented. The Blazor package is currently a planned surface with documentation only.