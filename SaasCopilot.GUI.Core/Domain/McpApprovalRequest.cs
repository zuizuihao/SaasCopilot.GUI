using System;
using System.Collections.Generic;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpApprovalRequest
	{
		public required Uri Endpoint { get; init; }

		public required McpToolDescriptor Tool { get; init; }

		public required string ArgumentsJson { get; init; }

		public string? OriginalPrompt { get; init; }

		public AssistantToolCallIntent? ToolCallIntent { get; init; }

		public IReadOnlyList<AssistantConversationTurn> ConversationHistory { get; init; } = Array.Empty<AssistantConversationTurn>();

		public IReadOnlyList<AssistantToolExecution> ToolExecutionHistory { get; init; } = Array.Empty<AssistantToolExecution>();
	}
}
