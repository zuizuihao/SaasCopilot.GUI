using System;
using System.IO;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpLogService : IMcpLogService
	{
		readonly string logPath;

		public McpLogService(IMcpStoragePathProvider storagePathProvider)
		{
			ArgumentNullException.ThrowIfNull(storagePathProvider);

			logPath = Path.Combine(storagePathProvider.LogDirectoryPath, "sidecar.log");
		}

		public void Log(string category, string message)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
			File.AppendAllLines(logPath, new[] { $"{DateTimeOffset.UtcNow:O}\t{category}\t{message}" });
		}
	}
}
