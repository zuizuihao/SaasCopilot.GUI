namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantToolCallIntent
	{
		public string? ToolCallId { get; init; }

		public required string ToolName { get; init; }

		public required string ArgumentsJson { get; init; }
	}
}
