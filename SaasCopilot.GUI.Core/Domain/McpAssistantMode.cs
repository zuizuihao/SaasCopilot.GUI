namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public enum McpAssistantMode
	{
		/// <summary>The model answers directly from its own knowledge. MCP tools are never invoked.</summary>
		Chat,

		/// <summary>The model calls MCP tools only when the user explicitly requests one with #toolName.</summary>
		Act,
	}
}
