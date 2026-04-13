using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpHubConnection : IMcpHubConnection
	{
		HubConnection? connection;

		public event EventHandler<McpHubActionEventArgs>? ActionReceived;

		public async Task StartAsync(Uri hubUri, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(hubUri);

			await StopAsync();

			connection = new HubConnectionBuilder()
				.WithUrl(hubUri)
				.WithAutomaticReconnect()
				.Build();

			connection.On<McpHubActionEventArgs>("ActionCompleted", ev => ActionReceived?.Invoke(this, ev));
			connection.On<McpHubActionEventArgs>("ActionFailed", ev => ActionReceived?.Invoke(this, ev));

			await connection.StartAsync(cancellationToken);
		}

		public async Task StopAsync()
		{
			if (connection is null)
			{
				return;
			}

			var current = connection;
			connection = null;
			await current.StopAsync();
			await current.DisposeAsync();
		}

		public async ValueTask DisposeAsync()
		{
			await StopAsync();
		}
	}
}
