using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public static class McpDirectToolIntentParser
	{
		public static string StripToolMention(string prompt, string toolName)
		{
			ArgumentNullException.ThrowIfNull(prompt);
			ArgumentNullException.ThrowIfNull(toolName);

			var mention = "#" + toolName;
			var index = prompt.IndexOf(mention, StringComparison.OrdinalIgnoreCase);
			if (index < 0)
			{
				return prompt;
			}

			var after = index + mention.Length;
			if (after < prompt.Length && prompt[after] == ' ')
			{
				after++;
			}

			return (prompt[..index] + prompt[after..]).Trim();
		}

		public static bool TryBuildIntent(string prompt, McpToolDescriptor tool, out AssistantToolCallIntent? toolIntent)
		{
			ArgumentNullException.ThrowIfNull(prompt);
			ArgumentNullException.ThrowIfNull(tool);

			toolIntent = null;
			var argumentText = StripToolMention(prompt, tool.Name);
			if (string.IsNullOrWhiteSpace(argumentText))
			{
				return false;
			}

			if (TryParseJsonArguments(argumentText, out var argumentsJson))
			{
				toolIntent = CreateIntent(tool.Name, argumentsJson);
				return true;
			}

			return TryBuildBuiltInShortcutIntent(tool, argumentText, out toolIntent);
		}

		static bool TryBuildBuiltInShortcutIntent(McpToolDescriptor tool, string argumentText, out AssistantToolCallIntent? toolIntent)
		{
			toolIntent = tool.Name switch
			{
				"file_read" => CreateIntent(tool.Name, new JsonObject { ["path"] = argumentText }.ToJsonString()),
				"run_command" => CreateIntent(tool.Name, new JsonObject { ["command"] = argumentText }.ToJsonString()),
				"search_web" when Uri.TryCreate(argumentText, UriKind.Absolute, out var uri)
					&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
					=> CreateIntent(tool.Name, new JsonObject { ["url"] = uri.ToString() }.ToJsonString()),
				_ => null,
			};

			return toolIntent is not null;
		}

		static bool TryParseJsonArguments(string argumentText, out string argumentsJson)
		{
			argumentsJson = string.Empty;
			try
			{
				var node = JsonNode.Parse(argumentText);
				if (node is not JsonObject jsonObject)
				{
					return false;
				}

				argumentsJson = jsonObject.ToJsonString();
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		static AssistantToolCallIntent CreateIntent(string toolName, string argumentsJson)
		{
			return new AssistantToolCallIntent
			{
				ToolCallId = "call_direct_mention",
				ToolName = toolName,
				ArgumentsJson = argumentsJson,
			};
		}
	}
}