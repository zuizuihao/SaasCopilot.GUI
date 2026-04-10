using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IBuiltInToolExecutor
	{
		Task<McpToolCallResult> ExecuteAsync(McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default);
	}
}
