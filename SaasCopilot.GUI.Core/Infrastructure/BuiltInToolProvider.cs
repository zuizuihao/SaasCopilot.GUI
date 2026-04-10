using System.Collections.Generic;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class BuiltInToolProvider : IBuiltInToolProvider
	{
		static readonly IReadOnlyList<McpToolDescriptor> Tools = new[]
		{
			new McpToolDescriptor
			{
				Name = "file_read",
				Title = "Read File",
				Description = "Reads the text content of a file on the local machine.",
				InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Absolute or relative path to the file.\"}},\"required\":[\"path\"]}",
				ReadOnlyHint = true,
				Source = McpToolSource.BuiltIn,
			},
			new McpToolDescriptor
			{
				Name = "file_write",
				Title = "Write File",
				Description = "Creates or overwrites a file with the given text content on the local machine.",
				InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Absolute or relative path to the file.\"},\"content\":{\"type\":\"string\",\"description\":\"Text content to write.\"}},\"required\":[\"path\",\"content\"]}",
				DestructiveHint = true,
				Source = McpToolSource.BuiltIn,
			},
			new McpToolDescriptor
			{
				Name = "run_command",
				Title = "Run Command",
				Description = "Executes a shell command on the local machine and returns its output.",
				InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"Shell command to execute.\"},\"working_directory\":{\"type\":\"string\",\"description\":\"Optional working directory for the command.\"}},\"required\":[\"command\"]}",
				DestructiveHint = true,
				Source = McpToolSource.BuiltIn,
			},
			new McpToolDescriptor
			{
				Name = "search_web",
				Title = "Search Web",
				Description = "Fetches the text content of an HTTP or HTTPS URL.",
				InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"HTTP or HTTPS URL to fetch.\"}},\"required\":[\"url\"]}",
				ReadOnlyHint = true,
				OpenWorldHint = true,
				Source = McpToolSource.BuiltIn,
			},
		};

		public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;
	}
}
