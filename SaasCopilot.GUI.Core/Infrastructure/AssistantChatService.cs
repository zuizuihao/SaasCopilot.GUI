using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantChatService : IAssistantChatService
	{
		public const string HttpClientName = "assistant-chat";

		static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(45);
		const int CompactToolResultCharacterLimit = 12000;
		const int TokenEstimateCharsPerToken = 4;
		const int MinimumRetainedHistoryTurns = 2;

		readonly IHttpClientFactory httpClientFactory;
		readonly IMcpLogService logService;
		readonly ILogger<AssistantChatService> logger;
		readonly TimeSpan requestTimeout;

		[ActivatorUtilitiesConstructor]
		public AssistantChatService(IHttpClientFactory httpClientFactory, IMcpLogService logService, ILogger<AssistantChatService> logger)
			: this(httpClientFactory, logService, logger, DefaultRequestTimeout)
		{
		}

		public AssistantChatService(IHttpClientFactory httpClientFactory, IMcpLogService logService, ILogger<AssistantChatService> logger, TimeSpan requestTimeout)
		{
			ArgumentNullException.ThrowIfNull(httpClientFactory);
			ArgumentNullException.ThrowIfNull(logService);
			ArgumentNullException.ThrowIfNull(logger);
			ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);

			this.httpClientFactory = httpClientFactory;
			this.logService = logService;
			this.logger = logger;
			this.requestTimeout = requestTimeout;
		}

		public async Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);
			ArgumentNullException.ThrowIfNull(request.Model);
			ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

			var prompt = request.Prompt.Trim();
			if (HasToolFollowUp(request))
			{
				return await GenerateToolFollowUpResponseAsync(request, onMessageDelta, cancellationToken).ConfigureAwait(false);
			}

			if (TryBuildDirectToolIntent(request, prompt, out var directToolIntent, out var directMessage))
			{
				return new AssistantChatResponse
				{
					Message = directMessage,
					ToolCallIntent = directToolIntent,
				};
			}

			if (TryGetPinnedTool(request, out var pinnedTool))
			{
				return await GeneratePinnedToolResponseAsync(request, pinnedTool, cancellationToken).ConfigureAwait(false);
			}

			var (responseBody, isSuccess) = await TrySendAsync(BuildPayload(request), request, "prompt", onMessageDelta, cancellationToken).ConfigureAwait(false);

			if (!isSuccess && IsContextOverflowError(responseBody))
			{
				logService.Log("model-warn", $"Context overflow for '{request.Model.Id}'; retrying without tools.");
				(responseBody, isSuccess) = await TrySendAsync(BuildPayload(request, omitTools: true), request, "prompt-retry", onMessageDelta, cancellationToken).ConfigureAwait(false);
			}

			if (!isSuccess)
			{
				logService.Log("model-error", responseBody);
				throw new InvalidOperationException($"LM Studio request failed: {responseBody}");
			}

			var parsedResponse = ParseAssistantResponse(responseBody, request, "chat");

			logService.Log("model-response", $"Model '{request.Model.Id}' returned {(parsedResponse.ToolCallIntent is null ? "a direct response" : $"tool intent '{parsedResponse.ToolCallIntent.ToolName}'")}. ");

			return parsedResponse;
		}

		async Task<AssistantChatResponse> GenerateToolFollowUpResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta, CancellationToken cancellationToken)
		{
			var toolExecutionHistory = GetToolExecutionHistory(request);
			logService.Log("model-follow-up", $"Sending follow-up request for model '{request.Model.Id}' after {toolExecutionHistory.Count} tool turn(s).");
			var (responseBody, isSuccess) = await TrySendAsync(BuildToolFollowUpPayload(request), request, "follow-up", onMessageDelta, cancellationToken).ConfigureAwait(false);
			if (!isSuccess && IsContextOverflowError(responseBody))
			{
				logService.Log("model-warn", $"Context overflow for follow-up '{request.Model.Id}'; retrying with compact tool results.");
				(responseBody, isSuccess) = await TrySendAsync(BuildToolFollowUpPayload(request, compactToolResults: true), request, "follow-up-retry", onMessageDelta, cancellationToken).ConfigureAwait(false);
			}

			if (!isSuccess)
			{
				logService.Log("model-error", responseBody);
				throw new InvalidOperationException($"LM Studio follow-up request failed: {responseBody}");
			}

			var parsedResponse = ParseAssistantResponse(responseBody, request, "follow-up chat");
			var responseMessage = string.IsNullOrWhiteSpace(parsedResponse.Message) && parsedResponse.ToolCallIntent is null
				? request.ToolResultText ?? "Tool completed."
				: parsedResponse.Message;
			logService.Log("model-follow-up-response", $"Model '{request.Model.Id}' returned {(parsedResponse.ToolCallIntent is null ? "a follow-up response" : $"tool intent '{parsedResponse.ToolCallIntent.ToolName}'")} after {toolExecutionHistory.Count} tool turn(s).");

			return new AssistantChatResponse
			{
				Message = responseMessage,
				ToolCallIntent = parsedResponse.ToolCallIntent,
			};
		}

		async Task<AssistantChatResponse> GeneratePinnedToolResponseAsync(AssistantChatRequest request, McpToolDescriptor pinnedTool, CancellationToken cancellationToken)
		{
			var (responseBody, isSuccess) = await TrySendAsync(BuildPinnedToolPayload(request, pinnedTool), request, "pinned-tool", null, cancellationToken).ConfigureAwait(false);
			if (!isSuccess && IsContextOverflowError(responseBody))
			{
				logService.Log("model-warn", $"Context overflow for pinned tool '{pinnedTool.Name}' on '{request.Model.Id}'; retrying without conversation history.");
				(responseBody, isSuccess) = await TrySendAsync(BuildPinnedToolPayload(request, pinnedTool, omitConversationHistory: true), request, "pinned-tool-retry", null, cancellationToken).ConfigureAwait(false);
			}

			if (!isSuccess)
			{
				logService.Log("model-error", responseBody);
				throw new InvalidOperationException($"LM Studio request failed: {responseBody}");
			}

			var parsedResponse = ParseAssistantResponse(responseBody, request, "pinned tool chat");
			if (parsedResponse.ToolCallIntent is not null)
			{
				var validatedToolCallArguments = ValidatePinnedToolArguments(parsedResponse.ToolCallIntent.ArgumentsJson, pinnedTool, out var validationMessage);
				if (validatedToolCallArguments is null)
				{
					return new AssistantChatResponse
					{
						Message = validationMessage,
					};
				}

				return new AssistantChatResponse
				{
					Message = string.Empty,
					ToolCallIntent = new AssistantToolCallIntent
					{
						ToolCallId = parsedResponse.ToolCallIntent.ToolCallId,
						ToolName = pinnedTool.Name,
						ArgumentsJson = validatedToolCallArguments,
					},
				};
			}

			if (TryParsePinnedToolArguments(parsedResponse.Message, pinnedTool, out var argumentsJson, out var message))
			{
				return new AssistantChatResponse
				{
					Message = string.Empty,
					ToolCallIntent = new AssistantToolCallIntent
					{
						ToolCallId = "call_pinned_model",
						ToolName = pinnedTool.Name,
						ArgumentsJson = argumentsJson,
					},
				};
			}

			return new AssistantChatResponse
			{
				Message = message,
			};
		}

		async Task<(string Body, bool IsSuccess)> TrySendAsync(JsonObject payload, AssistantChatRequest request, string requestKind, Action<string>? onMessageDelta, CancellationToken cancellationToken)
		{
			var streamResponse = onMessageDelta is not null;
			payload["stream"] = streamResponse;
			var payloadJson = payload.ToJsonString(SerializerOptions);
			var requestId = Guid.NewGuid().ToString("N")[..8];
			var toolExecutionHistory = GetToolExecutionHistory(request);
			logService.Log("model-http", $"[{requestId}] Sending {requestKind} request to '{request.Model.Id}' (messages={payload["messages"]?.AsArray().Count ?? 0}, tools={payload["tools"]?.AsArray().Count ?? 0}, toolTurns={toolExecutionHistory.Count}, payloadChars={payloadJson.Length}).");

			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(requestTimeout);
			var httpClient = httpClientFactory.CreateClient(HttpClientName);
			using var requestMessage = new HttpRequestMessage(HttpMethod.Post, LmStudioLocalServer.ChatCompletionsUri);
			requestMessage.Headers.TryAddWithoutValidation("Authorization", $"Bearer {LmStudioLocalServer.ApiKey}");
			requestMessage.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

			try
			{
				using var response = await httpClient.SendAsync(
					requestMessage,
					streamResponse ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
					timeoutCts.Token).ConfigureAwait(false);
				var body = streamResponse && response.IsSuccessStatusCode
					? await ReadStreamingResponseBodyAsync(response, requestId, requestKind, onMessageDelta!, timeoutCts.Token).ConfigureAwait(false)
					: await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
				logService.Log("model-http", $"[{requestId}] LM Studio returned {(int)response.StatusCode} {response.StatusCode} for {requestKind} request (bodyChars={body.Length}, streamed={streamResponse && response.IsSuccessStatusCode}).");
				return (body, response.IsSuccessStatusCode);
			}
			catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
			{
				var timeoutMessage = BuildTimeoutMessage(requestKind);
				logService.Log("model-timeout", $"[{requestId}] {timeoutMessage} Model '{request.Model.Id}'.");
				throw new InvalidOperationException(timeoutMessage, ex);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				logService.Log("model-cancel", $"[{requestId}] Canceled {requestKind} request for model '{request.Model.Id}'.");
				throw;
			}
		}

		async Task<string> ReadStreamingResponseBodyAsync(HttpResponseMessage response, string requestId, string requestKind, Action<string> onMessageDelta, CancellationToken cancellationToken)
		{
			using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			using var reader = new StreamReader(responseStream, Encoding.UTF8, true, 4096, leaveOpen: false);

			var rawBodyBuilder = new StringBuilder();
			var streamedMessage = new StringBuilder();
			var eventDataLines = new List<string>();
			var toolCalls = new SortedDictionary<int, StreamedToolCallState>();
			var sawEventStream = false;

			while (true)
			{
				var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
				if (line is null)
				{
					if (eventDataLines.Count > 0)
					{
						sawEventStream = true;
						if (ProcessServerSentEvent(eventDataLines, streamedMessage, toolCalls, onMessageDelta))
						{
							break;
						}
					}

					break;
				}

				rawBodyBuilder.AppendLine(line);
				if (line.Length == 0)
				{
					if (eventDataLines.Count == 0)
					{
						continue;
					}

					sawEventStream = true;
					if (ProcessServerSentEvent(eventDataLines, streamedMessage, toolCalls, onMessageDelta))
					{
						break;
					}

					eventDataLines.Clear();
					continue;
				}

				if (line.StartsWith("data:", StringComparison.Ordinal))
				{
					eventDataLines.Add(line[5..].TrimStart());
				}
			}

			if (!sawEventStream)
			{
				return rawBodyBuilder.ToString();
			}

			logService.Log("model-stream", $"[{requestId}] Streamed {streamedMessage.Length} response chars and {toolCalls.Count} tool call delta(s) for {requestKind} request.");
			return BuildStreamingResponseBody(streamedMessage.ToString(), toolCalls);
		}

		static bool ProcessServerSentEvent(List<string> eventDataLines, StringBuilder streamedMessage, SortedDictionary<int, StreamedToolCallState> toolCalls, Action<string> onMessageDelta)
		{
			if (eventDataLines.Count == 0)
			{
				return false;
			}

			var eventPayload = string.Join("\n", eventDataLines).Trim();
			eventDataLines.Clear();
			if (eventPayload.Length == 0)
			{
				return false;
			}

			if (string.Equals(eventPayload, "[DONE]", StringComparison.Ordinal))
			{
				return true;
			}

			var rootNode = JsonNode.Parse(eventPayload);
			var choices = rootNode?["choices"]?.AsArray();
			var deltaNode = choices is not null && choices.Count > 0 ? choices[0]?["delta"] : null;
			if (deltaNode is null)
			{
				return false;
			}

			var messageDelta = ExtractMessageText(deltaNode["content"]);
			if (!string.IsNullOrEmpty(messageDelta))
			{
				streamedMessage.Append(messageDelta);
				onMessageDelta(messageDelta);
			}

			AccumulateStreamingToolCalls(deltaNode["tool_calls"]?.AsArray(), toolCalls);
			return false;
		}

		static void AccumulateStreamingToolCalls(JsonArray? toolCallDeltas, SortedDictionary<int, StreamedToolCallState> toolCalls)
		{
			if (toolCallDeltas is null)
			{
				return;
			}

			foreach (var toolCallDelta in toolCallDeltas)
			{
				var index = toolCallDelta?["index"]?.GetValue<int>() ?? 0;
				if (!toolCalls.TryGetValue(index, out var toolCallState))
				{
					toolCallState = new StreamedToolCallState();
					toolCalls[index] = toolCallState;
				}

				var id = toolCallDelta?["id"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(id))
				{
					toolCallState.Id = id;
				}

				var functionNode = toolCallDelta?["function"];
				var name = functionNode?["name"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name))
				{
					toolCallState.Name = name;
				}

				var argumentsDelta = functionNode?["arguments"]?.GetValue<string>();
				if (!string.IsNullOrEmpty(argumentsDelta))
				{
					toolCallState.Arguments.Append(argumentsDelta);
				}
			}
		}

		static string BuildStreamingResponseBody(string assistantMessage, SortedDictionary<int, StreamedToolCallState> toolCalls)
		{
			var messageNode = new JsonObject
			{
				["content"] = assistantMessage,
			};

			var toolCallsArray = new JsonArray();
			foreach (var toolCallState in toolCalls.Values)
			{
				if (string.IsNullOrWhiteSpace(toolCallState.Name))
				{
					continue;
				}

				toolCallsArray.Add(new JsonObject
				{
					["id"] = string.IsNullOrWhiteSpace(toolCallState.Id) ? null : toolCallState.Id,
					["type"] = "function",
					["function"] = new JsonObject
					{
						["name"] = toolCallState.Name,
						["arguments"] = toolCallState.Arguments.Length == 0 ? "{}" : toolCallState.Arguments.ToString(),
					},
				});
			}

			if (toolCallsArray.Count > 0)
			{
				messageNode["tool_calls"] = toolCallsArray;
			}

			return new JsonObject
			{
				["choices"] = new JsonArray
				{
					new JsonObject
					{
						["message"] = messageNode,
					},
				},
			}.ToJsonString(SerializerOptions);
		}

		AssistantChatResponse ParseAssistantResponse(string responseBody, AssistantChatRequest request, string responseKind)
		{
			JsonNode? rootNode;
			try
			{
				rootNode = JsonNode.Parse(responseBody);
			}
			catch (JsonException ex)
			{
				logger.LogWarning(ex, "LM Studio returned invalid JSON for {responseKind}.", responseKind);
				throw new InvalidOperationException("LM Studio returned invalid JSON.", ex);
			}

			var choices = rootNode?["choices"]?.AsArray();
			var messageNode = choices is not null && choices.Count > 0 ? choices[0]?["message"] : null;
			if (messageNode is null)
			{
				throw new InvalidOperationException($"LM Studio did not return a {responseKind} message.");
			}

			var assistantMessage = ExtractAssistantMessage(messageNode);
			var inferredToolIntent = ExtractInferredToolIntent(request, messageNode);
			if (inferredToolIntent is null && McpToolRequestTextParser.TryParse(assistantMessage, out var taggedToolIntent))
			{
				inferredToolIntent = ValidateTaggedToolIntent(request, taggedToolIntent);
				assistantMessage = string.Empty;
			}

			if (string.IsNullOrWhiteSpace(assistantMessage))
			{
				assistantMessage = inferredToolIntent is null
					? "LM Studio returned an empty response."
					: string.Empty;
			}

			return new AssistantChatResponse
			{
				Message = assistantMessage,
				ToolCallIntent = inferredToolIntent,
			};
		}

		string BuildTimeoutMessage(string requestKind)
		{
			return string.Equals(requestKind, "follow-up", StringComparison.Ordinal)
				? $"LM Studio follow-up request timed out after {requestTimeout.TotalSeconds:0} seconds while processing the tool result."
				: $"LM Studio request timed out after {requestTimeout.TotalSeconds:0} seconds.";
		}

		static bool HasToolFollowUp(AssistantChatRequest request) => GetToolExecutionHistory(request).Count > 0;

		static IReadOnlyList<AssistantToolExecution> GetToolExecutionHistory(AssistantChatRequest request)
		{
			if (request.ToolExecutionHistory.Count > 0)
			{
				return request.ToolExecutionHistory;
			}

			if (request.ToolCallIntent is null || string.IsNullOrWhiteSpace(request.ToolResultText))
			{
				return Array.Empty<AssistantToolExecution>();
			}

			return new[]
			{
				new AssistantToolExecution
				{
					ToolCallIntent = request.ToolCallIntent,
					ToolResultText = request.ToolResultText,
					ToolResultRawJson = request.ToolResultRawJson,
				},
			};
		}

		static bool IsContextOverflowError(string responseBody) =>
			responseBody.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);

		static IReadOnlyList<AssistantConversationTurn> PruneConversationHistory(
			IReadOnlyList<AssistantConversationTurn> history,
			string currentPrompt,
			string systemPrompt,
			int? contextWindow)
		{
			if (contextWindow is null || history.Count <= MinimumRetainedHistoryTurns)
			{
				return history;
			}

			var contextWindowChars = contextWindow.Value * TokenEstimateCharsPerToken;
			var fixedChars = systemPrompt.Length + currentPrompt.Length;
			var working = new List<AssistantConversationTurn>(history);

			while (working.Count > MinimumRetainedHistoryTurns)
			{
				var historyChars = 0;
				for (var i = 0; i < working.Count; i++)
				{
					historyChars += working[i].Content.Length;
				}

				if (fixedChars + historyChars <= contextWindowChars)
				{
					break;
				}

				// Remove the oldest pair (up to 2 turns), preserving the minimum tail.
				var toRemove = Math.Min(2, working.Count - MinimumRetainedHistoryTurns);
				working.RemoveRange(0, toRemove);
			}

			return working;
		}

		static JsonObject BuildPayload(AssistantChatRequest request, bool omitTools = false)
		{
			var toolsForRequest = GetToolsForRequest(request);
			var systemPrompt = BuildSystemPrompt(request);
			var history = PruneConversationHistory(request.ConversationHistory, request.Prompt, systemPrompt, request.Model.ContextWindow);
			var messages = new JsonArray
			{
				new JsonObject
				{
					["role"] = "system",
					["content"] = systemPrompt,
				},
			};

			foreach (var turn in history)
			{
				messages.Add(new JsonObject
				{
					["role"] = turn.Role,
					["content"] = turn.Content,
				});
			}

			messages.Add(new JsonObject
			{
				["role"] = "user",
				["content"] = request.Prompt,
			});

			var payload = new JsonObject
			{
				["model"] = request.Model.Id,
				["messages"] = messages,
				["temperature"] = 0.2,
				["stream"] = false,
			};

			if (toolsForRequest.Count > 0 && !omitTools)
			{
				payload["tools"] = BuildToolDefinitions(toolsForRequest);
				payload["tool_choice"] = BuildToolChoice(request);
				payload["parallel_tool_calls"] = false;
			}

			return payload;
		}

		static JsonObject BuildPinnedToolPayload(AssistantChatRequest request, McpToolDescriptor pinnedTool, bool omitConversationHistory = false)
		{
			var messages = new JsonArray
			{
				new JsonObject
				{
					["role"] = "system",
					["content"] = BuildPinnedToolSystemPrompt(pinnedTool),
				},
			};

			if (!omitConversationHistory)
			{
				foreach (var turn in request.ConversationHistory)
				{
					messages.Add(new JsonObject
					{
						["role"] = turn.Role,
						["content"] = turn.Content,
					});
				}
			}

			messages.Add(new JsonObject
			{
				["role"] = "user",
				["content"] = request.Prompt,
			});

			return new JsonObject
			{
				["model"] = request.Model.Id,
				["messages"] = messages,
				["temperature"] = 0.2,
				["stream"] = false,
			};
		}

		static JsonObject BuildToolFollowUpPayload(AssistantChatRequest request, bool compactToolResults = false)
		{
			var toolsForRequest = GetToolsForRequest(request);
			var toolExecutions = GetToolExecutionHistory(request);
			var latestToolExecution = toolExecutions.Count > 0 ? toolExecutions[^1] : null;
			var toolName = latestToolExecution?.ToolCallIntent.ToolName ?? request.ToolCallIntent?.ToolName ?? "the requested tool";

			var messages = new JsonArray
			{
				new JsonObject
				{
					["role"] = "system",
					["content"] = BuildSystemPrompt(request),
				},
			};

			foreach (var turn in request.ConversationHistory)
			{
				messages.Add(new JsonObject
				{
					["role"] = turn.Role,
					["content"] = turn.Content,
				});
			}

			messages.Add(new JsonObject
			{
				["role"] = "user",
				["content"] = request.Prompt,
			});

			foreach (var toolExecution in toolExecutions)
			{
				var toolCallId = string.IsNullOrWhiteSpace(toolExecution.ToolCallIntent.ToolCallId) ? "call_local" : toolExecution.ToolCallIntent.ToolCallId;
				var toolResultContent = BuildToolResultContentForFollowUp(toolExecution, compactToolResults);
				messages.Add(new JsonObject
				{
					["role"] = "assistant",
					["content"] = string.Empty,
					["tool_calls"] = new JsonArray
					{
						new JsonObject
						{
							["id"] = toolCallId,
							["type"] = "function",
							["function"] = new JsonObject
							{
								["name"] = toolExecution.ToolCallIntent.ToolName,
								["arguments"] = toolExecution.ToolCallIntent.ArgumentsJson,
							},
						},
					},
				});
				messages.Add(new JsonObject
				{
					["role"] = "tool",
					["tool_call_id"] = toolCallId,
					["content"] = toolResultContent,
				});
			}

			messages.Add(new JsonObject
			{
				["role"] = "user",
				["content"] = BuildToolFollowUpInstruction(request, toolName),
			});

			var payload = new JsonObject
			{
				["model"] = request.Model.Id,
				["messages"] = messages,
				["temperature"] = 0.2,
				["stream"] = false,
			};

			if (toolsForRequest.Count > 0)
			{
				payload["tools"] = BuildToolDefinitions(toolsForRequest);
				payload["tool_choice"] = "auto";
				payload["parallel_tool_calls"] = false;
			}

			return payload;
		}

		static string BuildToolResultContentForFollowUp(AssistantToolExecution toolExecution, bool compactToolResults)
		{
			var toolResultContent = McpJsonFormatting.NormalizeToolResultForModel(toolExecution.ToolResultRawJson, toolExecution.ToolResultText);
			if (!compactToolResults || toolResultContent.Length <= CompactToolResultCharacterLimit)
			{
				return toolResultContent;
			}

			var trimmedContent = toolResultContent[..CompactToolResultCharacterLimit].TrimEnd();
			return trimmedContent + Environment.NewLine + Environment.NewLine + "[Tool result truncated to fit the model context window. If more detail is required, ask for a narrower inspection or smaller result set.]";
		}

		static string BuildToolFollowUpInstruction(AssistantChatRequest request, string toolName)
		{
			var builder = new StringBuilder();
			builder.Append("The tool '");
			builder.Append(toolName);
			builder.AppendLine("' has finished and its output is in the previous tool message.");
			builder.AppendLine("Read that tool output carefully before answering.");
			builder.Append("Answer the user's request: ");
			builder.AppendLine(request.Prompt);
			builder.Append("Base the answer on the tool output. If the tool output is empty or insufficient, say that plainly. If another listed tool is required to finish the task, emit a tool call instead of guessing.");
			return builder.ToString();
		}

		static string BuildSystemPrompt(AssistantChatRequest request)
		{
			if (!string.IsNullOrWhiteSpace(request.SystemPromptOverride))
			{
				return request.SystemPromptOverride;
			}

			var toolsForRequest = GetToolsForRequest(request);

			var sb = new StringBuilder();
			sb.AppendLine("You are the SaasCopilot desktop assistant.");
			sb.AppendLine();

			sb.AppendLine(request.Endpoint is null
				? "No MCP endpoint is currently connected."
				: $"Active MCP endpoint: {request.Endpoint}");
			sb.AppendLine();

			if (toolsForRequest.Count == 0)
			{
				sb.AppendLine("No MCP tools are available. Answer the user's question directly from your own knowledge.");
				sb.AppendLine("Do not emit tool calls or tool-call syntax in your response. Respond with natural language only.");
			}
			else
			{
				sb.Append(toolsForRequest.Count);
				sb.AppendLine(" MCP tools are available.");
				sb.AppendLine();
				sb.AppendLine("Tool selection rules:");
				sb.AppendLine("- Use a tool only for live app data, server data, or explicit app actions.");
				sb.AppendLine("- Answer directly for explanation, drafting, or reasoning tasks.");
				sb.AppendLine("- Ask if required arguments are missing. Only call listed tools.");
				if (!string.IsNullOrWhiteSpace(request.PinnedToolName))
				{
					sb.Append("- The user explicitly requested '");
					sb.Append(request.PinnedToolName);
					sb.AppendLine("'. Emit exactly that tool call and no other tool.");
				}
				sb.AppendLine();
				sb.AppendLine("Available tools:");
				sb.Append(BuildTinyToolCatalogSummary(toolsForRequest));

				sb.AppendLine();
				sb.AppendLine("Response rules:");
				sb.AppendLine("- When you need a tool, emit a tool call. Do not describe the call in prose.");
				sb.Append("- Base your answer on the actual tool result. Keep responses concise and operational.");
			}

			return sb.ToString();
		}

		static string BuildPinnedToolSystemPrompt(McpToolDescriptor tool)
		{
			var builder = new StringBuilder();
			builder.AppendLine("You are preparing arguments for a user-selected MCP tool.");
			builder.Append("The selected tool is '");
			builder.Append(tool.Name);
			builder.AppendLine("'. Do not choose, suggest, or mention any other tool.");
			if (!string.IsNullOrWhiteSpace(tool.Description))
			{
				builder.Append("Tool description: ");
				builder.AppendLine(tool.Description.Trim());
			}

			builder.AppendLine("Return exactly one of these outputs:");
			builder.AppendLine("- A raw JSON object containing arguments for this tool only.");
			builder.AppendLine("- A brief clarification question if the user has not provided enough information.");
			builder.AppendLine("Do not wrap JSON in markdown fences.");
			builder.AppendLine("Do not emit tool-call syntax.");
			builder.AppendLine("Use only keys defined by this schema and omit unknown keys.");
			builder.Append("Tool input schema: ");
			builder.AppendLine(string.IsNullOrWhiteSpace(tool.InputSchemaJson) ? "{}" : tool.InputSchemaJson);
			return builder.ToString();
		}

		static IReadOnlyList<McpToolDescriptor> GetToolsForRequest(AssistantChatRequest request)
		{
			if (request.AvailableTools.Count == 0)
			{
				return Array.Empty<McpToolDescriptor>();
			}

			if (!string.IsNullOrWhiteSpace(request.PinnedToolName))
			{
				return request.AvailableTools
					.Where(t => string.Equals(t.Name, request.PinnedToolName, StringComparison.Ordinal))
					.ToList();
			}

			var toolNames = GetToolNamesInTurn(request);
			if (toolNames.Count == 0)
			{
				return request.AvailableTools;
			}

			return request.AvailableTools
				.Where(t => toolNames.Contains(t.Name))
				.ToList();
		}

		static bool TryGetPinnedTool(AssistantChatRequest request, out McpToolDescriptor pinnedTool)
		{
			var toolsForRequest = GetToolsForRequest(request);
			if (string.IsNullOrWhiteSpace(request.PinnedToolName) || toolsForRequest.Count != 1)
			{
				pinnedTool = null!;
				return false;
			}

			pinnedTool = toolsForRequest[0];
			return true;
		}

		static HashSet<string> GetToolNamesInTurn(AssistantChatRequest request)
		{
			var toolNames = new HashSet<string>(StringComparer.Ordinal);
			foreach (var toolExecution in GetToolExecutionHistory(request))
			{
				if (!string.IsNullOrWhiteSpace(toolExecution.ToolCallIntent.ToolName))
				{
					toolNames.Add(toolExecution.ToolCallIntent.ToolName);
				}
			}

			if (request.ToolCallIntent is not null && !string.IsNullOrWhiteSpace(request.ToolCallIntent.ToolName))
			{
				toolNames.Add(request.ToolCallIntent.ToolName);
			}

			return toolNames;
		}

		static JsonNode BuildToolChoice(AssistantChatRequest request)
		{
			if (string.IsNullOrWhiteSpace(request.PinnedToolName))
			{
				return JsonValue.Create("auto")!;
			}

			return JsonValue.Create("required")!;
		}

		static JsonArray BuildToolDefinitions(IReadOnlyList<McpToolDescriptor> availableTools)
		{
			var tools = new JsonArray();
			foreach (var tool in availableTools)
			{
				tools.Add(new JsonObject
				{
					["type"] = "function",
					["function"] = new JsonObject
					{
						["name"] = tool.Name,
						["description"] = BuildToolDefinitionDescription(tool),
						["parameters"] = ParseParameters(tool.InputSchemaJson),
					},
				});
			}

			return tools;
		}

		static string BuildToolCatalogSummary(IReadOnlyList<McpToolDescriptor> availableTools)
		{
			var summary = new StringBuilder();
			for (var index = 0; index < availableTools.Count; index++)
			{
				if (index > 0)
				{
					summary.Append('\n');
				}

				summary.Append("- ");
				summary.Append(BuildToolSummary(availableTools[index]));
			}

			return summary.ToString();
		}

		static string BuildTinyToolCatalogSummary(IReadOnlyList<McpToolDescriptor> availableTools)
		{
			var summary = new StringBuilder();
			for (var index = 0; index < availableTools.Count; index++)
			{
				if (index > 0)
				{
					summary.Append('\n');
				}

				summary.Append("- ");
				summary.Append(BuildTinyToolSummary(availableTools[index]));
			}

			return summary.ToString();
		}

		static string BuildToolDefinitionDescription(McpToolDescriptor tool)
		{
			// Prefer the MCP title field as a human-readable prefix when it differs from the name.
			var title = !string.IsNullOrWhiteSpace(tool.Title) && !string.Equals(tool.Title, tool.Name, StringComparison.OrdinalIgnoreCase)
				? tool.Title.Trim()
				: null;
			var description = string.IsNullOrWhiteSpace(tool.Description) ? "MCP tool" : tool.Description.Trim();
			var hints = BuildToolHints(tool);
			// Parameter details are intentionally omitted here — they are already present in the tool's
			// parameters schema and duplicating them in the description inflates the token count significantly.
			var hasOutputSchema = !string.IsNullOrWhiteSpace(tool.OutputSchemaJson) && tool.OutputSchemaJson != "{}";
			if (title is null && string.IsNullOrEmpty(hints) && !hasOutputSchema)
			{
				return description;
			}

			var builder = new StringBuilder();
			if (title is not null)
			{
				builder.Append(title);
				builder.Append(": ");
			}

			builder.Append(description);
			if (!string.IsNullOrEmpty(hints))
			{
				builder.Append(" Hints: ");
				builder.Append(hints);
				builder.Append('.');
			}

			if (hasOutputSchema)
			{
				builder.Append(" Returns structured JSON matching the outputSchema.");
			}

			return builder.ToString();
		}

		// Lean catalog line used in the system prompt: name (title): description [hints].
		// Parameter details are intentionally omitted here — they are already in the tools
		// array schemas and duplicating them inflates the prompt token count significantly.
		static string BuildToolSummary(McpToolDescriptor tool)
		{
			var builder = new StringBuilder();
			builder.Append(tool.Name);

			var title = !string.IsNullOrWhiteSpace(tool.Title) && !string.Equals(tool.Title, tool.Name, StringComparison.OrdinalIgnoreCase)
				? tool.Title.Trim()
				: null;
			if (title is not null)
			{
				builder.Append(" (");
				builder.Append(title);
				builder.Append(')');
			}

			var description = string.IsNullOrWhiteSpace(tool.Description) ? null : tool.Description.Trim();
			if (!string.IsNullOrEmpty(description))
			{
				builder.Append(": ");
				builder.Append(description);
			}

			var hints = BuildToolHints(tool);
			if (!string.IsNullOrEmpty(hints))
			{
				builder.Append(" [");
				builder.Append(hints);
				builder.Append(']');
			}

			return builder.ToString();
		}

		static string BuildTinyToolSummary(McpToolDescriptor tool)
		{
			var builder = new StringBuilder();
			builder.Append(tool.Name);

			var description = !string.IsNullOrWhiteSpace(tool.Description)
				? tool.Description.Trim()
				: !string.IsNullOrWhiteSpace(tool.Title)
					? tool.Title.Trim()
					: null;
			if (!string.IsNullOrEmpty(description))
			{
				builder.Append(": ");
				builder.Append(description.Length <= 60 ? description : description[..57] + "...");
			}

			return builder.ToString();
		}

		static string BuildToolHints(McpToolDescriptor tool)
		{
			var hints = new List<string>();
			if (tool.ReadOnlyHint)
			{
				hints.Add("read-only");
			}

			if (tool.DestructiveHint)
			{
				hints.Add("destructive");
			}

			if (tool.IdempotentHint)
			{
				hints.Add("idempotent");
			}

			if (tool.OpenWorldHint)
			{
				hints.Add("open-world");
			}

			return string.Join(", ", hints);
		}

		static string BuildParameterSummary(McpToolDescriptor tool, int maxParameters)
		{
			var parameters = ParseParameters(tool.InputSchemaJson) as JsonObject;
			var properties = parameters?["properties"] as JsonObject;
			if (properties is null || properties.Count == 0)
			{
				return string.Empty;
			}

			var requiredNames = new HashSet<string>(StringComparer.Ordinal);
			var required = parameters?["required"] as JsonArray;
			if (required is not null)
			{
				foreach (var item in required)
				{
					var name = item?.GetValue<string>();
					if (!string.IsNullOrWhiteSpace(name))
					{
						requiredNames.Add(name);
					}
				}
			}

			var builder = new StringBuilder();
			var count = 0;
			foreach (var property in properties)
			{
				if (count > 0)
				{
					builder.Append("; ");
				}

				builder.Append(BuildParameterEntry(property.Key, property.Value, requiredNames.Contains(property.Key)));
				count++;
				if (count >= maxParameters)
				{
					break;
				}
			}

			if (properties.Count > count)
			{
				builder.Append("; ...");
			}

			return builder.ToString();
		}

		static string BuildParameterEntry(string name, JsonNode? schemaNode, bool isRequired)
		{
			var builder = new StringBuilder();
			builder.Append(name);

			var typeText = GetSchemaTypeText(schemaNode);
			if (!string.IsNullOrEmpty(typeText))
			{
				builder.Append(':');
				builder.Append(typeText);
			}

			if (isRequired)
			{
				builder.Append(" required");
			}

			var description = GetSchemaDescription(schemaNode);
			if (!string.IsNullOrEmpty(description))
			{
				builder.Append(" - ");
				builder.Append(description);
			}

			return builder.ToString();
		}

		static string GetSchemaTypeText(JsonNode? schemaNode)
		{
			if (schemaNode is not JsonObject schemaObject)
			{
				return string.Empty;
			}

			if (schemaObject["enum"] is JsonArray enumValues && enumValues.Count > 0)
			{
				var builder = new StringBuilder("enum(");
				for (var index = 0; index < enumValues.Count && index < 3; index++)
				{
					if (index > 0)
					{
						builder.Append('|');
					}

					builder.Append(enumValues[index]?.ToJsonString() ?? "null");
				}

				if (enumValues.Count > 3)
				{
					builder.Append("|...");
				}

				builder.Append(')');
				return builder.ToString();
			}

			if (schemaObject["type"] is JsonArray typeArray && typeArray.Count > 0)
			{
				var types = new List<string>();
				foreach (var item in typeArray)
				{
					var typeName = item?.GetValue<string>();
					if (!string.IsNullOrWhiteSpace(typeName))
					{
						types.Add(typeName);
					}
				}

				return types.Count == 0 ? string.Empty : string.Join("|", types);
			}

			return schemaObject["type"]?.GetValue<string>() ?? string.Empty;
		}

		static string GetSchemaDescription(JsonNode? schemaNode)
		{
			var description = schemaNode?["description"]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(description))
			{
				return string.Empty;
			}

			var trimmed = description.Trim();
			return trimmed.Length <= 80 ? trimmed : trimmed[..77] + "...";
		}

		static JsonNode ParseParameters(string inputSchemaJson)
		{
			if (string.IsNullOrWhiteSpace(inputSchemaJson))
			{
				return new JsonObject();
			}

			try
			{
				return JsonNode.Parse(inputSchemaJson) ?? new JsonObject();
			}
			catch (JsonException)
			{
				return new JsonObject();
			}
		}

		static string ExtractAssistantMessage(JsonNode messageNode)
		{
			return ExtractMessageText(messageNode["content"]);
		}

		static string ExtractMessageText(JsonNode? contentNode)
		{
			if (contentNode is null)
			{
				return string.Empty;
			}

			if (contentNode is JsonValue)
			{
				return contentNode.GetValue<string>();
			}

			if (contentNode is not JsonArray contentArray)
			{
				return contentNode.ToJsonString(SerializerOptions);
			}

			var segments = new List<string>();
			foreach (var item in contentArray)
			{
				var type = item?["type"]?.GetValue<string>();
				if (!string.Equals(type, "text", StringComparison.Ordinal))
				{
					continue;
				}

				var text = item?["text"]?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(text))
				{
					segments.Add(text);
				}
			}

			return string.Join(Environment.NewLine + Environment.NewLine, segments);
		}

		static bool TryParsePinnedToolArguments(string responseText, McpToolDescriptor pinnedTool, out string argumentsJson, out string message)
		{
			argumentsJson = string.Empty;
			message = string.IsNullOrWhiteSpace(responseText) ? "LM Studio returned an empty response." : responseText;
			if (string.IsNullOrWhiteSpace(responseText))
			{
				return false;
			}

			try
			{
				var parsed = JsonNode.Parse(NormalizeJsonResponseText(responseText));
				if (parsed is not JsonObject jsonObject)
				{
					return false;
				}

				var validatedArguments = ValidatePinnedToolArguments(jsonObject.ToJsonString(SerializerOptions), pinnedTool, out message);
				if (validatedArguments is null)
				{
					return false;
				}

				argumentsJson = validatedArguments;
				message = string.Empty;
				return true;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		static string NormalizeJsonResponseText(string responseText)
		{
			var trimmed = responseText.Trim();
			if (!trimmed.StartsWith("```", StringComparison.Ordinal))
			{
				return trimmed;
			}

			var firstNewLine = trimmed.IndexOf("\n", StringComparison.Ordinal);
			if (firstNewLine < 0)
			{
				return trimmed;
			}

			var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
			if (lastFence <= firstNewLine)
			{
				return trimmed;
			}

			return trimmed[(firstNewLine + 1)..lastFence].Trim();
		}

		static string? ValidatePinnedToolArguments(string argumentsJson, McpToolDescriptor pinnedTool, out string message)
		{
			message = string.Empty;
			JsonObject argumentsObject;
			try
			{
				var parsed = string.IsNullOrWhiteSpace(argumentsJson) ? new JsonObject() : JsonNode.Parse(argumentsJson);
				if (parsed is not JsonObject jsonObject)
				{
					message = $"Tool '{pinnedTool.Name}' arguments must be a JSON object.";
					return null;
				}

				argumentsObject = jsonObject;
			}
			catch (JsonException)
			{
				message = $"Tool '{pinnedTool.Name}' arguments must be valid JSON.";
				return null;
			}

			var schema = ParseParameters(pinnedTool.InputSchemaJson) as JsonObject;
			ApplySchemaDefaults(argumentsObject, schema);
			var missingRequiredArguments = GetMissingRequiredArguments(argumentsObject, schema);
			if (missingRequiredArguments.Count > 0)
			{
				message = $"Tool '{pinnedTool.Name}' needs required arguments: {string.Join(", ", missingRequiredArguments)}.";
				return null;
			}

			return argumentsObject.ToJsonString(SerializerOptions);
		}

		static void ApplySchemaDefaults(JsonObject argumentsObject, JsonObject? schema)
		{
			var properties = schema?["properties"] as JsonObject;
			if (properties is null)
			{
				return;
			}

			foreach (var property in properties)
			{
				if (argumentsObject.ContainsKey(property.Key))
				{
					continue;
				}

				if (property.Value is JsonObject propertySchema && propertySchema["default"] is JsonNode defaultValue)
				{
					argumentsObject[property.Key] = defaultValue.DeepClone();
				}
			}
		}

		static List<string> GetMissingRequiredArguments(JsonObject argumentsObject, JsonObject? schema)
		{
			var missingArguments = new List<string>();
			var requiredProperties = schema?["required"] as JsonArray;
			if (requiredProperties is null)
			{
				return missingArguments;
			}

			var properties = schema?["properties"] as JsonObject;
			foreach (var requiredProperty in requiredProperties)
			{
				var name = requiredProperty?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				argumentsObject.TryGetPropertyValue(name, out var value);
				var propertySchema = properties?[name] as JsonObject;
				if (IsMissingRequiredValue(value, propertySchema))
				{
					missingArguments.Add(name);
				}
			}

			return missingArguments;
		}

		static bool IsMissingRequiredValue(JsonNode? value, JsonObject? propertySchema)
		{
			if (value is null)
			{
				return !AllowsNull(propertySchema);
			}

			if (value is JsonValue jsonValue
				&& jsonValue.TryGetValue<string>(out var stringValue)
				&& string.IsNullOrWhiteSpace(stringValue))
			{
				return true;
			}

			return false;
		}

		static bool AllowsNull(JsonObject? propertySchema)
		{
			if (propertySchema is null)
			{
				return false;
			}

			if (propertySchema["type"] is JsonValue typeValue)
			{
				return string.Equals(typeValue.GetValue<string>(), "null", StringComparison.Ordinal);
			}

			if (propertySchema["type"] is not JsonArray typeArray)
			{
				return false;
			}

			foreach (var type in typeArray)
			{
				if (string.Equals(type?.GetValue<string>(), "null", StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		static AssistantToolCallIntent? ExtractInferredToolIntent(AssistantChatRequest request, JsonNode messageNode)
		{
			var toolCalls = messageNode["tool_calls"]?.AsArray();
			if (toolCalls is null || toolCalls.Count == 0)
			{
				return null;
			}

			foreach (var toolCall in toolCalls)
			{
				var toolName = toolCall?["function"]?["name"]?.GetValue<string>();
				if (string.IsNullOrWhiteSpace(toolName))
				{
					continue;
				}

				var matchedTool = false;
				foreach (var tool in request.AvailableTools)
				{
					if (string.Equals(tool.Name, toolName, StringComparison.Ordinal))
					{
						matchedTool = true;
						break;
					}
				}

				if (!matchedTool)
				{
					throw new InvalidOperationException($"LM Studio requested unknown MCP tool '{toolName}'.");
				}

				var argumentsJson = toolCall?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
				var toolCallId = toolCall?["id"]?.GetValue<string>();
				try
				{
					_ = string.IsNullOrWhiteSpace(argumentsJson) ? new JsonObject() : JsonNode.Parse(argumentsJson);
				}
				catch (JsonException ex)
				{
					throw new InvalidOperationException($"LM Studio returned invalid arguments for tool '{toolName}'.", ex);
				}

				return new AssistantToolCallIntent
				{
					ToolCallId = toolCallId,
					ToolName = toolName,
					ArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson,
				};
			}

			return null;
		}

		static AssistantToolCallIntent ValidateTaggedToolIntent(AssistantChatRequest request, AssistantToolCallIntent? toolIntent)
		{
			if (toolIntent is null)
			{
				throw new InvalidOperationException("LM Studio returned an empty tagged tool request.");
			}

			foreach (var tool in request.AvailableTools)
			{
				if (string.Equals(tool.Name, toolIntent.ToolName, StringComparison.Ordinal))
				{
					return toolIntent;
				}
			}

			throw new InvalidOperationException($"LM Studio requested unknown MCP tool '{toolIntent.ToolName}'.");
		}

		sealed class StreamedToolCallState
		{
			public string? Id { get; set; }

			public string? Name { get; set; }

			public StringBuilder Arguments { get; } = new StringBuilder();
		}

		static bool TryBuildDirectToolIntent(AssistantChatRequest request, string prompt, out AssistantToolCallIntent? toolIntent, out string message)
		{
			toolIntent = null;
			message = string.Empty;

			if (!prompt.StartsWith("/tool ", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var commandBody = prompt[6..].Trim();
			if (commandBody.Length == 0)
			{
				message = "Tool command format is '/tool <name> <json>'.";
				return true;
			}

			var separatorIndex = commandBody.IndexOf(' ', StringComparison.Ordinal);
			var toolName = separatorIndex >= 0 ? commandBody[..separatorIndex].Trim() : commandBody;
			var argumentsJson = separatorIndex >= 0 ? commandBody[(separatorIndex + 1)..].Trim() : "{}";

			if (string.IsNullOrWhiteSpace(toolName))
			{
				message = "Tool command format is '/tool <name> <json>'.";
				return true;
			}

			McpToolDescriptor? selectedTool = null;
			foreach (var tool in request.AvailableTools)
			{
				if (string.Equals(tool.Name, toolName, StringComparison.Ordinal))
				{
					selectedTool = tool;
					break;
				}
			}

			if (selectedTool is null)
			{
				message = $"Tool '{toolName}' is not available. Connect and refresh tools before sending a tool command.";
				return true;
			}

			try
			{
				_ = string.IsNullOrWhiteSpace(argumentsJson) ? new JsonObject() : JsonNode.Parse(argumentsJson);
			}
			catch (JsonException)
			{
				message = "Tool arguments must be valid JSON.";
				return true;
			}

			toolIntent = new AssistantToolCallIntent
			{
				ToolCallId = "call_direct",
				ToolName = selectedTool.Name,
				ArgumentsJson = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson,
			};

			message = $"Selected MCP tool '{selectedTool.Name}' directly. The call is ready and will request approval if needed.";
			return true;
		}
	}
}
