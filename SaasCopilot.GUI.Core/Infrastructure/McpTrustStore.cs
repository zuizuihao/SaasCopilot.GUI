using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpTrustStore : IMcpTrustStore
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
		};

		readonly string trustPath;

		public McpTrustStore(IMcpStoragePathProvider storagePathProvider)
		{
			ArgumentNullException.ThrowIfNull(storagePathProvider);

			trustPath = storagePathProvider.TrustPath;
		}

		public bool IsTrusted(Uri endpoint, string toolName)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

			return LoadEntries().Any(entry =>
				string.Equals(entry.EndpointAuthority, endpoint.Authority, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
		}

		public void SaveTrust(Uri endpoint, string toolName)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

			var entries = LoadEntries();
			if (entries.Any(entry =>
				string.Equals(entry.EndpointAuthority, endpoint.Authority, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(entry.ToolName, toolName, StringComparison.OrdinalIgnoreCase)))
			{
				return;
			}

			entries.Add(new McpTrustEntry
			{
				EndpointAuthority = endpoint.Authority,
				ToolName = toolName,
				ApprovedAtUtc = DateTimeOffset.UtcNow,
			});

			Directory.CreateDirectory(Path.GetDirectoryName(trustPath)!);
			File.WriteAllText(trustPath, JsonSerializer.Serialize(entries, SerializerOptions));
		}

		List<McpTrustEntry> LoadEntries()
		{
			if (!File.Exists(trustPath))
			{
				return new List<McpTrustEntry>();
			}

			try
			{
				var json = File.ReadAllText(trustPath);
				return JsonSerializer.Deserialize<List<McpTrustEntry>>(json, SerializerOptions) ?? new List<McpTrustEntry>();
			}
			catch (IOException)
			{
				return new List<McpTrustEntry>();
			}
			catch (UnauthorizedAccessException)
			{
				return new List<McpTrustEntry>();
			}
			catch (JsonException)
			{
				return new List<McpTrustEntry>();
			}
			catch (NotSupportedException)
			{
				return new List<McpTrustEntry>();
			}
		}
	}
}
