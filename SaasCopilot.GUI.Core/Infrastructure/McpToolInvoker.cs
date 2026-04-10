using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpToolInvoker : IMcpToolInvoker
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
		};

		readonly IMcpConnectionManager connectionManager;
		readonly IBuiltInToolExecutor builtInToolExecutor;

		public McpToolInvoker(IMcpConnectionManager connectionManager, IBuiltInToolExecutor builtInToolExecutor)
		{
			ArgumentNullException.ThrowIfNull(connectionManager);
			ArgumentNullException.ThrowIfNull(builtInToolExecutor);

			this.connectionManager = connectionManager;
			this.builtInToolExecutor = builtInToolExecutor;
		}

		public async Task<McpToolCallResult> InvokeAsync(Uri endpoint, McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentNullException.ThrowIfNull(tool);
			ArgumentNullException.ThrowIfNull(argumentsJson);

			if (tool.Source == McpToolSource.BuiltIn)
			{
				return await builtInToolExecutor.ExecuteAsync(tool, argumentsJson, cancellationToken).ConfigureAwait(false);
			}

			JsonNode? argumentsNode;
			try
			{
				argumentsNode = string.IsNullOrWhiteSpace(argumentsJson) ? new JsonObject() : JsonNode.Parse(argumentsJson);
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException("Tool arguments must be valid JSON.", ex);
			}

			var result = await connectionManager.SendRequestAsync(
				endpoint,
				"tools/call",
				new JsonObject
				{
					["name"] = tool.Name,
					["arguments"] = argumentsNode ?? new JsonObject(),
				},
				cancellationToken);

			var displayText = BuildDisplayText(result);
			if (result?["isError"]?.GetValue<bool>() == true)
			{
				throw new InvalidOperationException(displayText);
			}

			return new McpToolCallResult
			{
				Succeeded = true,
				DisplayText = displayText,
				RawJson = result?.ToJsonString(SerializerOptions) ?? "{}",
			};
		}

		static string BuildDisplayText(JsonNode? result)
		{
			var content = result?["content"] as JsonArray;
			if (content is null || content.Count == 0)
			{
				return McpJsonFormatting.NormalizeText(result?.ToJsonString(SerializerOptions) ?? "{}", out _);
			}

			var lines = new List<string>();
			foreach (var item in content)
			{
				var type = item?["type"]?.GetValue<string>();
				if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
				{
					lines.Add(McpJsonFormatting.NormalizeText(item?["text"]?.GetValue<string>() ?? string.Empty, out _));
				}
				else
				{
					lines.Add(McpJsonFormatting.NormalizeText(item?.ToJsonString(SerializerOptions) ?? string.Empty, out _));
				}
			}

			return string.Join(Environment.NewLine + Environment.NewLine, lines);
		}
	}
}
