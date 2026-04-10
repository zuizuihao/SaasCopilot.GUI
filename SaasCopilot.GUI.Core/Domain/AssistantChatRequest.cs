using System;
using System.Collections.Generic;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantChatRequest
	{
		public required string Prompt { get; init; }

		public required AssistantModelDescriptor Model { get; init; }

		public Uri? Endpoint { get; init; }

		public required IReadOnlyList<McpToolDescriptor> AvailableTools { get; init; }

		public IReadOnlyList<AssistantConversationTurn> ConversationHistory { get; init; } = Array.Empty<AssistantConversationTurn>();

		public AssistantToolCallIntent? ToolCallIntent { get; init; }

		public IReadOnlyList<AssistantToolExecution> ToolExecutionHistory { get; init; } = Array.Empty<AssistantToolExecution>();

		public string? ToolResultText { get; init; }

		public string? ToolResultRawJson { get; init; }

		/// <summary>
		/// When set, the request locks the turn to this tool. The service will either
		/// build a direct intent immediately or ask the model for arguments for this
		/// exact tool without exposing a broader tool catalog.
		/// </summary>
		public string? PinnedToolName { get; init; }

		/// <summary>
		/// When set the service uses this string verbatim as the system prompt, bypassing the
		/// normal SaasCopilot assistant prompt. Use this for classifier or routing calls that
		/// need a different persona than the main assistant.
		/// </summary>
		public string? SystemPromptOverride { get; init; }
	}
}
