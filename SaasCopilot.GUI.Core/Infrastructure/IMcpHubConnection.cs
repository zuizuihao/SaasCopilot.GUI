using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpHubConnection : IAsyncDisposable
	{
		event EventHandler<McpHubActionEventArgs>? ActionReceived;

		Task StartAsync(Uri hubUri, CancellationToken cancellationToken = default);

		Task StopAsync();
	}
}
