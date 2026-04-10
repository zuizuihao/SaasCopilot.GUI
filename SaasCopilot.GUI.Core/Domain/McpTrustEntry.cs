using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpTrustEntry
	{
		public required string EndpointAuthority { get; init; }

		public required string ToolName { get; init; }

		public required DateTimeOffset ApprovedAtUtc { get; init; }
	}
}
