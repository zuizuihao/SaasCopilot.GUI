using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpHubActionEventArgs : EventArgs
	{
		public required string Tool { get; init; }

		public required string Key { get; init; }

		public required bool Success { get; init; }
	}
}
