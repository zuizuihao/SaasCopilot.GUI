namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpToolDescriptor
	{
		public required string Name { get; init; }

		/// <summary>Human-readable display name from the MCP <c>title</c> field. Prefer over <see cref="Name"/> when presenting the tool to users or models.</summary>
		public string? Title { get; init; }

		public string? Description { get; init; }

		public string InputSchemaJson { get; init; } = "{}";

		public string? OutputSchemaJson { get; init; }

		public bool ReadOnlyHint { get; init; }

		public bool DestructiveHint { get; init; }

		public bool IdempotentHint { get; init; }

		public bool OpenWorldHint { get; init; }

		public McpToolSource Source { get; init; } = McpToolSource.Remote;
	}
}
