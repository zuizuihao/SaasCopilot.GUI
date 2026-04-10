using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpApprovalService
	{
		bool RequiresApproval(Uri endpoint, McpToolDescriptor tool);

		void SaveTrust(Uri endpoint, McpToolDescriptor tool);

		void RecordDecision(Uri endpoint, McpToolDescriptor tool, bool approved, bool remembered);
	}
}
