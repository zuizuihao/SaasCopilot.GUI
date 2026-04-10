using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public static class McpJsonFormatting
	{
		static readonly JsonSerializerOptions PrettyJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
		};

		public static string NormalizeText(string text, out bool usesStructuredFormatting)
		{
			usesStructuredFormatting = false;
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}

			var trimmed = text.Trim();
			if (TryParsePossiblyEncodedStructuredJson(trimmed, out var structuredJson))
			{
				usesStructuredFormatting = true;
				return Serialize(structuredJson);
			}

			if (TryDecodeJsonString(trimmed, out var decodedText))
			{
				var decodedTrimmed = decodedText.Trim();
				if (TryParsePossiblyEncodedStructuredJson(decodedTrimmed, out structuredJson))
				{
					usesStructuredFormatting = true;
					return Serialize(structuredJson);
				}

				return decodedText;
			}

			return text;
		}

		public static string NormalizeToolResultForModel(string? rawJson, string? displayText)
		{
			var rawJsonIsStructured = false;
			var normalizedRawJson = string.IsNullOrWhiteSpace(rawJson)
				? string.Empty
				: NormalizeText(rawJson, out rawJsonIsStructured);
			var displayTextIsStructured = false;
			var normalizedDisplayText = string.IsNullOrWhiteSpace(displayText)
				? string.Empty
				: NormalizeText(displayText, out displayTextIsStructured);

			if (string.IsNullOrWhiteSpace(normalizedRawJson) && string.IsNullOrWhiteSpace(normalizedDisplayText))
			{
				return "Tool returned no content.";
			}

			if (!string.IsNullOrWhiteSpace(normalizedRawJson) && !string.IsNullOrWhiteSpace(normalizedDisplayText))
			{
				if (string.Equals(normalizedRawJson.Trim(), normalizedDisplayText.Trim(), StringComparison.Ordinal))
				{
					return normalizedRawJson.Trim();
				}

				var builder = new StringBuilder();
				builder.AppendLine("Tool result:");
				builder.AppendLine(normalizedDisplayText.Trim());
				builder.AppendLine();
				builder.AppendLine(rawJsonIsStructured || displayTextIsStructured ? "Structured tool data:" : "Raw tool output:");
				builder.Append(normalizedRawJson.Trim());
				return builder.ToString();
			}

			if (!string.IsNullOrWhiteSpace(normalizedRawJson))
			{
				if (!rawJsonIsStructured)
				{
					return normalizedRawJson.Trim();
				}

				return "Structured tool data:" + Environment.NewLine + normalizedRawJson.Trim();
			}

			return normalizedDisplayText.Trim();
		}

		static string Serialize(JsonNode node)
		{
			return node.ToJsonString(PrettyJsonOptions);
		}

		static bool TryParsePossiblyEncodedStructuredJson(string text, out JsonNode structuredJson)
		{
			structuredJson = null!;
			if (TryParseStructuredJson(text, out structuredJson))
			{
				structuredJson = NormalizeStructuredJson(structuredJson);
				return true;
			}

			if (!TryDecodeJsonString(text, out var decodedText) || !TryParseStructuredJson(decodedText, out structuredJson))
			{
				return false;
			}

			structuredJson = NormalizeStructuredJson(structuredJson);
			return true;
		}

		static bool TryParseStructuredJson(string text, out JsonNode structuredJson)
		{
			structuredJson = null!;
			var trimmed = text.Trim();
			if (!LooksLikeStructuredJson(trimmed))
			{
				return false;
			}

			try
			{
				var parsed = JsonNode.Parse(trimmed);
				if (parsed is null)
				{
					return false;
				}

				structuredJson = parsed;
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		static bool LooksLikeStructuredJson(string text)
		{
			return text.Length >= 2 && ((text[0] == '{' && text[text.Length - 1] == '}') || (text[0] == '[' && text[text.Length - 1] == ']'));
		}

		static JsonNode NormalizeStructuredJson(JsonNode node)
		{
			return node switch
			{
				JsonObject jsonObject => NormalizeObject(jsonObject),
				JsonArray jsonArray => NormalizeArray(jsonArray),
				JsonValue jsonValue => NormalizeValue(jsonValue),
				_ => node.DeepClone(),
			};
		}

		static JsonObject NormalizeObject(JsonObject jsonObject)
		{
			var normalizedObject = new JsonObject();
			foreach (var property in jsonObject)
			{
				normalizedObject[property.Key] = property.Value is null ? null : NormalizeStructuredJson(property.Value);
			}

			return normalizedObject;
		}

		static JsonArray NormalizeArray(JsonArray jsonArray)
		{
			var normalizedArray = new JsonArray();
			for (var index = 0; index < jsonArray.Count; index++)
			{
				normalizedArray.Add(jsonArray[index] is null ? null : NormalizeStructuredJson(jsonArray[index]!));
			}

			return normalizedArray;
		}

		static JsonNode NormalizeValue(JsonValue jsonValue)
		{
			if (!jsonValue.TryGetValue<string>(out var stringValue) || !TryParsePossiblyEncodedStructuredJson(stringValue, out var structuredJson))
			{
				return jsonValue.DeepClone();
			}

			return structuredJson;
		}

		static bool TryDecodeJsonString(string text, out string decodedText)
		{
			decodedText = string.Empty;
			if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
			{
				return false;
			}

			try
			{
				decodedText = JsonSerializer.Deserialize<string>(text) ?? string.Empty;
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}
	}
}