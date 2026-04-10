using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpTranscriptStore : IMcpTranscriptStore
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

		readonly string transcriptDirectoryPath;
		readonly string transcriptFilePath;

		public McpTranscriptStore(IMcpStoragePathProvider storagePathProvider)
		{
			ArgumentNullException.ThrowIfNull(storagePathProvider);

			transcriptDirectoryPath = storagePathProvider.TranscriptDirectoryPath;
			transcriptFilePath = Path.Combine(transcriptDirectoryPath, $"{DateTime.UtcNow:yyyyMMdd}.jsonl");
		}

		public IReadOnlyList<McpTranscriptEntry> LoadRecent(int maxEntries)
		{
			if (maxEntries <= 0)
			{
				return Array.Empty<McpTranscriptEntry>();
			}

			if (!Directory.Exists(transcriptDirectoryPath))
			{
				return Array.Empty<McpTranscriptEntry>();
			}

			return Directory
				.EnumerateFiles(transcriptDirectoryPath, "*.jsonl")
				.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
				.SelectMany(ReadEntries)
				.Take(maxEntries)
				.Reverse()
				.ToArray();
		}

		public void Append(McpTranscriptEntry entry)
		{
			ArgumentNullException.ThrowIfNull(entry);

			Directory.CreateDirectory(Path.GetDirectoryName(transcriptFilePath)!);
			File.AppendAllLines(transcriptFilePath, new[] { JsonSerializer.Serialize(entry, SerializerOptions) });
		}

		public void Clear()
		{
			if (!Directory.Exists(transcriptDirectoryPath))
			{
				return;
			}

			foreach (var path in Directory.EnumerateFiles(transcriptDirectoryPath, "*.jsonl"))
			{
				File.Delete(path);
			}
		}

		static IEnumerable<McpTranscriptEntry> ReadEntries(string path)
		{
			foreach (var line in File.ReadLines(path).Reverse())
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				McpTranscriptEntry? entry;
				try
				{
					entry = JsonSerializer.Deserialize<McpTranscriptEntry>(line, SerializerOptions);
				}
				catch (JsonException)
				{
					continue;
				}

				if (entry is not null)
				{
					yield return entry;
				}
			}
		}
	}
}
