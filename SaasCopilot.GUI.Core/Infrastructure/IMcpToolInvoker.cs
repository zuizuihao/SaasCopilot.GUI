using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpToolInvoker
	{
		Task<McpToolCallResult> InvokeAsync(Uri endpoint, McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default);
	}
}
