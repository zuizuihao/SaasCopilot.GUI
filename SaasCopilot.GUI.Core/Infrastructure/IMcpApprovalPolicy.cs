using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpApprovalPolicy
	{
		bool RequiresApproval(Uri endpoint, McpToolDescriptor tool);
	}
}
