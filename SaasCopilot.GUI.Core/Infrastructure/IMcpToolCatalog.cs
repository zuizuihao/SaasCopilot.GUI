using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpToolCatalog
	{
		IReadOnlyList<McpToolDescriptor> GetBuiltInTools();

		Task<IReadOnlyList<McpToolDescriptor>> RefreshAsync(Uri endpoint, CancellationToken cancellationToken = default);
	}
}
