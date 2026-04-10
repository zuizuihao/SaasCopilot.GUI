# SaasCopilot.GUI.Core

Platform-agnostic core for the SaasCopilot AI copilot panel.

Contains all **domain models** and **infrastructure services** (MCP connection, LM Studio chat, tool catalogue, approval, transcript) with no dependency on any UI framework — shared by `SaasCopilot.GUI.WinForms` and `SaasCopilot.GUI.Blazor`.

For the repository overview and package map, see [README.md](../../README.md).

## Contents

| Layer | Description |
|---|---|
| `Domain/` | Immutable value types and enums (request/response models, tool descriptors, configuration) |
| `Infrastructure/` | Service implementations (HTTP calls to LM Studio, MCP session, JSON persistence) |
| `Application/` | `McpSidecarController` — orchestrates the chat loop, tool calls, approval, and transcript |

## See Also

- [README.md](../../README.md)
- [ARCHITECTURE.md](../../ARCHITECTURE.md)
- [SaasCopilot.GUI.WinForms](../SaasCopilot.GUI.WinForms/README.md)
