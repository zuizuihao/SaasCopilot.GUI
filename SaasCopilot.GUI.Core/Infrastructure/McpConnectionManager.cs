using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpConnectionManager : IMcpConnectionManager
	{
		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

		readonly IHttpClientFactory httpClientFactory;
		readonly ILogger<McpConnectionManager> logger;
		readonly IMcpLogService logService;

		Uri? connectedEndpoint;
		string? sessionId;
		bool initialized;
		long requestId;

		public McpConnectionManager(IHttpClientFactory httpClientFactory, ILogger<McpConnectionManager> logger, IMcpLogService logService)
		{
			ArgumentNullException.ThrowIfNull(httpClientFactory);
			ArgumentNullException.ThrowIfNull(logger);
			ArgumentNullException.ThrowIfNull(logService);

			this.httpClientFactory = httpClientFactory;
			this.logger = logger;
			this.logService = logService;
		}

		public async Task EnsureConnectedAsync(Uri endpoint, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(endpoint);

			if (initialized && connectedEndpoint == endpoint)
			{
				return;
			}

			connectedEndpoint = endpoint;
			sessionId = null;
			initialized = false;

			var initializeParams = new JsonObject
			{
				["protocolVersion"] = "2024-11-05",
				["capabilities"] = new JsonObject(),
				["clientInfo"] = new JsonObject
				{
					["name"] = "SaasCopilot.Client.Sidecar",
					["version"] = "0.1.0",
				},
			};

			_ = await SendCoreAsync(endpoint, "initialize", initializeParams, includeId: true, cancellationToken: cancellationToken);
			await SendCoreAsync(endpoint, "notifications/initialized", new JsonObject(), includeId: false, cancellationToken: cancellationToken);
			initialized = true;
			logService.Log("connection", $"Connected MCP session to '{endpoint}'.");
		}

		public async Task<JsonNode?> SendRequestAsync(Uri endpoint, string method, JsonObject? parameters, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(endpoint);
			ArgumentException.ThrowIfNullOrWhiteSpace(method);

			await EnsureConnectedAsync(endpoint, cancellationToken);
			return await SendCoreAsync(endpoint, method, parameters, includeId: true, cancellationToken: cancellationToken);
		}

		async Task<JsonNode?> SendCoreAsync(Uri endpoint, string method, JsonObject? parameters, bool includeId, CancellationToken cancellationToken)
		{
			using var httpClient = httpClientFactory.CreateClient();
			using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

			if (!string.IsNullOrWhiteSpace(sessionId))
			{
				request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
			}

			var payload = new JsonObject
			{
				["jsonrpc"] = "2.0",
				["method"] = method,
			};

			if (parameters is not null)
			{
				payload["params"] = parameters;
			}

			if (includeId)
			{
				payload["id"] = System.Threading.Interlocked.Increment(ref requestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
			}

			request.Content = new StringContent(payload.ToJsonString(SerializerOptions), Encoding.UTF8, "application/json");

			using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			response.EnsureSuccessStatusCode();

			if (response.Headers.TryGetValues("Mcp-Session-Id", out var headerValues))
			{
				sessionId = headerValues.FirstOrDefault();
			}

			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(body))
			{
				return null;
			}

			var jsonNode = response.Content.Headers.ContentType?.MediaType?.Contains("event-stream", StringComparison.OrdinalIgnoreCase) == true
				? ParseServerSentEvents(body)
				: JsonNode.Parse(body);

			if (jsonNode?["error"] is JsonNode errorNode)
			{
				var errorText = errorNode.ToJsonString(SerializerOptions);
				logger.LogWarning("MCP request {method} failed: {error}", method, errorText);
				logService.Log("protocol-error", $"{method}: {errorText}");
				throw new InvalidOperationException(errorText);
			}

			return jsonNode?["result"];
		}

		static JsonNode? ParseServerSentEvents(string body)
		{
			JsonNode? lastData = null;
			using var reader = new StringReader(body);
			string? line;
			while ((line = reader.ReadLine()) is not null)
			{
				if (!line.StartsWith("data:", StringComparison.Ordinal))
				{
					continue;
				}

				var payload = line.Substring(5).Trim();
				if (string.IsNullOrWhiteSpace(payload))
				{
					continue;
				}

				lastData = JsonNode.Parse(payload);
			}

			return lastData;
		}
	}
}
