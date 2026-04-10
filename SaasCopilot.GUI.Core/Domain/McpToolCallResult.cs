namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpToolCallResult
	{
		public bool Succeeded { get; init; }

		public string DisplayText { get; init; } = string.Empty;

		public string RawJson { get; init; } = string.Empty;
	}
}
