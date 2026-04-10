namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public enum McpConnectionState
	{
		Disabled,
		EndpointUnresolved,
		Disconnected,
		Connecting,
		GeneratingResponse,
		Connected,
		DiscoveryFailed,
		ModelResponseFailed,
		ToolInvocationFailed,
		ApprovalRequired,
	}
}
