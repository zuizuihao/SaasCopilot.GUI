using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpEndpointResolver
	{
		McpEndpointResolution Resolve(Uri? activeApplicationUri, string? endpointOverride);
	}
}
