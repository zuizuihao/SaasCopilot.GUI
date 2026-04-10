namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantChatResponse
	{
		public required string Message { get; init; }

		public AssistantToolCallIntent? ToolCallIntent { get; init; }
	}
}
