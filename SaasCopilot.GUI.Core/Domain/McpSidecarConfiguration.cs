namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpSidecarConfiguration
	{
		public bool IsVisible { get; init; } = true;

		public bool AutoConnectOnStartup { get; init; } = true;

		public int PanelWidth { get; init; } = 420;

		public string? EndpointOverride { get; init; }

		public string? SelectedModelId { get; init; }
	}
}
