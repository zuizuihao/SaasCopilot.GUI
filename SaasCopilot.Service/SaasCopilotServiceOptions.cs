namespace SaasCopilot.Service;

public sealed class SaasCopilotServiceOptions
{
	public string ServerName { get; set; } = "SaaS MCP Server";

	public string? ServerVersion { get; set; }

	public bool HttpTransportStateless { get; set; } = true;
}