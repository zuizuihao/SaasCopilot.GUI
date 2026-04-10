using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using SaasCopilot.Copilot.GUI.Features.Mcp;

// TestHost: minimal executable that opens McpSidecarForm driven by stub services.
// FlaUI attaches to this process by name to run UI tests.
//
// The process exits with code 0 when the window is closed normally.

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);

		var controller = new McpSidecarController(
			new StubEndpointResolver(),
			new StubConfigurationStore(),
			new StubModelSelectionService(),
			new StubChatService(),
			new StubTranscriptStore(),
			new StubToolCatalog(),
			new StubToolInvoker(),
			new StubApprovalService(),
			new StubLogService(),
			NullLogger<McpSidecarController>.Instance,
			() => new Uri("https://testhost.local/app"));

		var form = new McpSidecarForm(controller, () => Environment.Exit(0), "Saas Copilot")
		{
			ShowInTaskbar = true,
			StartPosition = FormStartPosition.CenterScreen,
			ClientSize = new System.Drawing.Size(980, 760),
		};

		Application.Run(form);
	}
}

// ── Stubs ──────────────────────────────────────────────────────────────────

sealed class StubEndpointResolver : IMcpEndpointResolver
{
	public McpEndpointResolution Resolve(Uri? activeApplicationUri, string? endpointOverride)
		=> McpEndpointResolution.Success(new Uri("https://testhost.local/mcp"));
}

sealed class StubConfigurationStore : IMcpConfigurationStore
{
	public McpSidecarConfiguration Load() => new McpSidecarConfiguration();
	public void Save(McpSidecarConfiguration configuration) { }
}

sealed class StubModelSelectionService : IAssistantModelSelectionService
{
	static readonly AssistantModelDescriptor[] Models =
	[
		new AssistantModelDescriptor { Id = "stub-model", DisplayName = "Stub Model" }
	];

	public IReadOnlyList<AssistantModelDescriptor> GetAvailableModels() => Models;
	public Task<IReadOnlyList<AssistantModelDescriptor>> RefreshAvailableModelsAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<AssistantModelDescriptor>>(Models);
	public AssistantModelDescriptor? GetSelectedModel() => Models[0];
	public AssistantModelDescriptor? SetSelectedModel(string? modelId) => Models[0];
}

sealed class StubChatService : IAssistantChatService
{
	public Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default)
		=> Task.FromResult(new AssistantChatResponse { Message = "Stub response." });
}

sealed class StubTranscriptStore : IMcpTranscriptStore
{
	readonly List<McpTranscriptEntry> entries = new();
	public IReadOnlyList<McpTranscriptEntry> LoadRecent(int maxEntries) => entries;
	public void Append(McpTranscriptEntry entry) => entries.Add(entry);
	public void Clear() => entries.Clear();
}

sealed class StubToolCatalog : IMcpToolCatalog
{
	static readonly IReadOnlyList<McpToolDescriptor> Empty = Array.Empty<McpToolDescriptor>();
	public IReadOnlyList<McpToolDescriptor> GetBuiltInTools() => Empty;
	public Task<IReadOnlyList<McpToolDescriptor>> RefreshAsync(Uri endpoint, CancellationToken cancellationToken = default)
		=> Task.FromResult(Empty);
}

sealed class StubToolInvoker : IMcpToolInvoker
{
	public Task<McpToolCallResult> InvokeAsync(Uri endpoint, McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default)
		=> throw new NotSupportedException("TestHost stub does not invoke real tools.");
}

sealed class StubApprovalService : IMcpApprovalService
{
	public bool RequiresApproval(Uri endpoint, McpToolDescriptor tool) => false;
	public void SaveTrust(Uri endpoint, McpToolDescriptor tool) { }
	public void RecordDecision(Uri endpoint, McpToolDescriptor tool, bool approved, bool remembered) { }
}

sealed class StubLogService : IMcpLogService
{
	public void Log(string category, string message) { }
}
