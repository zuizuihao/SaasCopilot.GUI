using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public static class McpToolRequestTextParser
	{
		const string ToolRequestStart = "[TOOL_REQUEST]";
		const string ToolRequestEnd = "[END_TOOL_REQUEST]";

		public static bool TryParse(string text, out AssistantToolCallIntent? toolIntent)
		{
			ArgumentNullException.ThrowIfNull(text);

			toolIntent = null;
			var startIndex = text.IndexOf(ToolRequestStart, StringComparison.Ordinal);
			if (startIndex < 0)
			{
				return false;
			}

			startIndex += ToolRequestStart.Length;
			var endIndex = text.IndexOf(ToolRequestEnd, startIndex, StringComparison.Ordinal);
			if (endIndex < 0)
			{
				return false;
			}

			var payloadText = text[startIndex..endIndex].Trim();
			if (payloadText.Length == 0)
			{
				return false;
			}

			try
			{
				var payload = JsonNode.Parse(payloadText) as JsonObject;
				var toolName = payload?["name"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(toolName))
				{
					return false;
				}

				var argumentsJson = payload?["arguments"] switch
				{
					JsonValue jsonValue when jsonValue.TryGetValue<string>(out var argumentsText) && !string.IsNullOrWhiteSpace(argumentsText)
						=> NormalizeArguments(argumentsText),
					JsonObject argumentsObject => argumentsObject.ToJsonString(),
					_ => "{}",
				};

				toolIntent = new AssistantToolCallIntent
				{
					ToolCallId = payload?["id"]?.GetValue<string>(),
					ToolName = toolName,
					ArgumentsJson = argumentsJson,
				};
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		static string NormalizeArguments(string argumentsText)
		{
			try
			{
				var parsed = JsonNode.Parse(argumentsText);
				return parsed?.ToJsonString() ?? "{}";
			}
			catch (JsonException)
			{
				return argumentsText;
			}
		}
	}
}