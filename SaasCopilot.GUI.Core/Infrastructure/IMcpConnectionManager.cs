using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpConnectionManager
	{
		Task EnsureConnectedAsync(Uri endpoint, CancellationToken cancellationToken = default);

		Task<JsonNode?> SendRequestAsync(Uri endpoint, string method, JsonObject? parameters, CancellationToken cancellationToken = default);
	}
}
