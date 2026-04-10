namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpConfigurationStore
	{
		McpSidecarConfiguration Load();

		void Save(McpSidecarConfiguration configuration);
	}
}
