using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpTranscriptEntry
	{
		public required DateTimeOffset Timestamp { get; init; }

		public required string Kind { get; init; }

		public string? Title { get; init; }

		public required string Message { get; init; }

		public string? Payload { get; init; }
	}
}
