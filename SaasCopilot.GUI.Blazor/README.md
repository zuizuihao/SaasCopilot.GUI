# SaasCopilot.GUI.Blazor

> **Status: planned** — Razor component equivalents of the WinForms controls in `SaasCopilot.GUI.WinForms` are not yet implemented. This project stub reserves the package identity and outlines the intended API.

Blazor component library for the SaasCopilot AI copilot panel. Provides the same chat UI as `SaasCopilot.GUI.WinForms` but as reusable Razor components for **Blazor Server**, **Blazor WebAssembly**, or **.NET MAUI Blazor Hybrid**.

> For the repository overview and package map see [README.md](../../README.md).
>
> For architecture details and roadmap see [ARCHITECTURE.md](../../ARCHITECTURE.md).

---

## Planned Components

| Component | WinForms equivalent | Description |
|---|---|---|
| `<McpSidecarPanel>` | `McpSidecarForm` | Top-level copilot panel |
| `<McpChatInput>` | `McpSidecarControl` | Chat input + response area + mode selector |
| `<McpTranscriptView>` | `McpChatTranscriptControl` | Scrollable tool/model message log |
| `<McpSettingsPanel>` | `McpSidecarSettingsForm` | Endpoint override + model picker |

---

## Planned Quick Start

```csharp
// Program.cs
builder.Services.AddSaasCopilotCopilotGui();
```

```razor
@* In your Blazor layout or page *@
<McpSidecarPanel AppBaseUri="@AppBaseUri" />
```

---

## See Also

- [README.md](../../README.md)
- [ARCHITECTURE.md](../../ARCHITECTURE.md)
- [SaasCopilot.GUI.Core](../SaasCopilot.GUI.Core/README.md) — shared domain and infrastructure
- [SaasCopilot.GUI.WinForms](../SaasCopilot.GUI.WinForms/README.md) — the shipping WinForms package
