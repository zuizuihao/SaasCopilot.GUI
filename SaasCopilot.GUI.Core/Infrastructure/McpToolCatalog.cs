using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpToolCatalog : IMcpToolCatalog
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

		readonly IMcpConnectionManager connectionManager;
		readonly IBuiltInToolProvider builtInToolProvider;

		public McpToolCatalog(IMcpConnectionManager connectionManager, IBuiltInToolProvider builtInToolProvider)
		{
			this.connectionManager = connectionManager;
			this.builtInToolProvider = builtInToolProvider;
		}

		public IReadOnlyList<McpToolDescriptor> GetBuiltInTools() => builtInToolProvider.GetTools();

		public async Task<IReadOnlyList<McpToolDescriptor>> RefreshAsync(Uri endpoint, CancellationToken cancellationToken = default)
		{
			var tools = new List<McpToolDescriptor>(builtInToolProvider.GetTools());
			string? cursor = null;

			do
			{
				var parameters = cursor is null ? new JsonObject() : new JsonObject { ["cursor"] = cursor };
				var result = await connectionManager.SendRequestAsync(endpoint, "tools/list", parameters, cancellationToken);
				var toolsNode = result?["tools"]?.AsArray();
				if (toolsNode is not null)
				{
					foreach (var item in toolsNode.OfType<JsonObject>())
					{
						var descriptor = TryBuildDescriptor(item);
						if (descriptor is not null)
						{
							tools.Add(descriptor);
						}
					}
				}

				cursor = result?["nextCursor"]?.GetValue<string>();
			}
			while (!string.IsNullOrEmpty(cursor));

			return tools;
		}

		static McpToolDescriptor? TryBuildDescriptor(JsonObject item)
		{
			var name = item["name"]?.GetValue<string>() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(name))
			{
				return null;
			}

			return new McpToolDescriptor
			{
				Name = name,
				Title = item["title"]?.GetValue<string>(),
				Description = item["description"]?.GetValue<string>(),
				InputSchemaJson = item["inputSchema"]?.ToJsonString(SerializerOptions) ?? "{}",
				OutputSchemaJson = item["outputSchema"]?.ToJsonString(SerializerOptions),
				ReadOnlyHint = item["annotations"]?["readOnlyHint"]?.GetValue<bool>() ?? false,
				DestructiveHint = item["annotations"]?["destructiveHint"]?.GetValue<bool>() ?? false,
				IdempotentHint = item["annotations"]?["idempotentHint"]?.GetValue<bool>() ?? false,
				OpenWorldHint = item["annotations"]?["openWorldHint"]?.GetValue<bool>() ?? false,
			};
		}
	}
}

