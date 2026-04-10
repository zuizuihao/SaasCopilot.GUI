namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantToolExecution
	{
		public required AssistantToolCallIntent ToolCallIntent { get; init; }

		public string? ToolResultText { get; init; }

		public string? ToolResultRawJson { get; init; }
	}
}