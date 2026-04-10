using System;
using System.IO;
using System.Text.Json;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpConfigurationStore : IMcpConfigurationStore
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
		};

		readonly IMcpStoragePathProvider storagePathProvider;

		public McpConfigurationStore(IMcpStoragePathProvider storagePathProvider)
		{
			ArgumentNullException.ThrowIfNull(storagePathProvider);

			this.storagePathProvider = storagePathProvider;
		}

		public McpSidecarConfiguration Load()
		{
			var path = storagePathProvider.ConfigurationPath;

			if (!File.Exists(path))
			{
				return new McpSidecarConfiguration();
			}

			try
			{
				var json = File.ReadAllText(path);
				return JsonSerializer.Deserialize<McpSidecarConfiguration>(json, SerializerOptions) ?? new McpSidecarConfiguration();
			}
			catch (IOException)
			{
				return new McpSidecarConfiguration();
			}
			catch (UnauthorizedAccessException)
			{
				return new McpSidecarConfiguration();
			}
			catch (JsonException)
			{
				return new McpSidecarConfiguration();
			}
			catch (NotSupportedException)
			{
				return new McpSidecarConfiguration();
			}
		}

		public void Save(McpSidecarConfiguration configuration)
		{
			ArgumentNullException.ThrowIfNull(configuration);

			var path = storagePathProvider.ConfigurationPath;
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(configuration, SerializerOptions));
		}
	}
}
