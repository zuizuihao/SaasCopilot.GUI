using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpApprovalService : IMcpApprovalService
	{
		readonly IMcpApprovalPolicy approvalPolicy;
		readonly IMcpTrustStore trustStore;
		readonly IMcpLogService logService;

		public McpApprovalService(IMcpApprovalPolicy approvalPolicy, IMcpTrustStore trustStore, IMcpLogService logService)
		{
			ArgumentNullException.ThrowIfNull(approvalPolicy);
			ArgumentNullException.ThrowIfNull(trustStore);
			ArgumentNullException.ThrowIfNull(logService);

			this.approvalPolicy = approvalPolicy;
			this.trustStore = trustStore;
			this.logService = logService;
		}

		public bool RequiresApproval(Uri endpoint, McpToolDescriptor tool)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentNullException.ThrowIfNull(tool);

			return approvalPolicy.RequiresApproval(endpoint, tool) && !trustStore.IsTrusted(endpoint, tool.Name);
		}

		public void SaveTrust(Uri endpoint, McpToolDescriptor tool)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentNullException.ThrowIfNull(tool);

			trustStore.SaveTrust(endpoint, tool.Name);
		}

		public void RecordDecision(Uri endpoint, McpToolDescriptor tool, bool approved, bool remembered)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentNullException.ThrowIfNull(tool);

			var suffix = approved
				? remembered
					? " Approved and trusted for future calls."
					: " Approved for a single call."
				: " Declined by the user.";

			logService.Log("approval", $"Tool '{tool.Name}' for '{endpoint}'.{suffix}");
		}
	}
}
