using System.Collections.Generic;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IBuiltInToolProvider
	{
		IReadOnlyList<McpToolDescriptor> GetTools();
	}
}
