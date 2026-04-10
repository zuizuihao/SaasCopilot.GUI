using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpApprovalPolicy : IMcpApprovalPolicy
	{
		public bool RequiresApproval(Uri endpoint, McpToolDescriptor tool)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentNullException.ThrowIfNull(tool);

			if (tool.ReadOnlyHint)
			{
				return false;
			}

			return !(tool.Name.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
				|| tool.Name.StartsWith("list_", StringComparison.OrdinalIgnoreCase)
				|| tool.Name.StartsWith("read_", StringComparison.OrdinalIgnoreCase)
				|| tool.Name.StartsWith("search_", StringComparison.OrdinalIgnoreCase)
				|| tool.Name.StartsWith("fetch_", StringComparison.OrdinalIgnoreCase));
		}
	}
}
