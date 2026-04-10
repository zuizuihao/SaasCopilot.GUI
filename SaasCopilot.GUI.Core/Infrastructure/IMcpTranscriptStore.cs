using System.Collections.Generic;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpTranscriptStore
	{
		IReadOnlyList<McpTranscriptEntry> LoadRecent(int maxEntries);

		void Append(McpTranscriptEntry entry);

		void Clear();
	}
}
