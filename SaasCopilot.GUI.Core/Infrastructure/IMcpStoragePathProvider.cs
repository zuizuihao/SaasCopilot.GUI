namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpStoragePathProvider
	{
		string ConfigurationPath { get; }

		string TrustPath { get; }

		string TranscriptDirectoryPath { get; }

		string LogDirectoryPath { get; }
	}
}
