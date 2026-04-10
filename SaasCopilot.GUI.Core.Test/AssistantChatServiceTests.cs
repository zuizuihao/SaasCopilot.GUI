using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	[TestFixture]
	[SupportedOSPlatform("windows10.0.17763.0")]
	public class AssistantChatServiceTests
	{
		[Test]
		public async Task GenerateResponseAsyncWhenFollowUpReturnsToolCallReturnsSecondToolIntentAsync()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
					(_, _) => Task.FromResult(CreateJsonResponse(new JsonObject
			{
				["choices"] = new JsonArray
				{
					new JsonObject
					{
						["message"] = new JsonObject
						{
							["content"] = string.Empty,
							["tool_calls"] = new JsonArray
							{
								new JsonObject
								{
									["id"] = "call_second",
									["type"] = "function",
									["function"] = new JsonObject
									{
										["name"] = "run_command",
										["arguments"] = "{\"command\":\"git status --short\"}",
									},
								},
							},
						},
					},
				},
			})),
					logService);

			var response = await service.GenerateResponseAsync(BuildFollowUpRequest());

			Assert.That(response.ToolCallIntent, Is.Not.Null);
			Assert.That(response.ToolCallIntent!.ToolCallId, Is.EqualTo("call_second"));
			Assert.That(response.ToolCallIntent.ToolName, Is.EqualTo("run_command"));
			Assert.That(response.ToolCallIntent.ArgumentsJson, Is.EqualTo("{\"command\":\"git status --short\"}"));
			Assert.That(response.Message, Is.Empty);
			Assert.That(logService.Messages, Has.Some.Contains("model-follow-up-response"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenResponseStreamsEmitsMessageDeltasAsync()
		{
			string? capturedBody = null;
			var observedDeltas = new List<string>();
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateStreamingResponse(
						"{\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}",
						"{\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}");
				},
				logService);

			var response = await service.GenerateResponseAsync(
				new AssistantChatRequest
				{
					Prompt = "Say hello.",
					Model = new AssistantModelDescriptor
					{
						Id = "google/gemma-3-4b",
						DisplayName = "Gemma 3 4B",
					},
					AvailableTools = Array.Empty<McpToolDescriptor>(),
				},
				observedDeltas.Add);

			Assert.That(response.Message, Is.EqualTo("Hello"));
			Assert.That(observedDeltas, Is.EqualTo(new[] { "Hel", "lo" }));
			Assert.That(capturedBody, Does.Contain("\"stream\":true"));
			Assert.That(logService.Messages, Has.Some.Contains("model-stream"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenToolCallStreamsBuildsToolIntentAsync()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
				(_, _) => Task.FromResult(
					CreateStreamingResponse(
						"""
						{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_stream","type":"function","function":{"name":"run_command","arguments":"{\"command\":\"git"}}]}}]}
						""",
						"""
						{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":" status --short\"}"}}]}}]}
						""")),
				logService);

			var response = await service.GenerateResponseAsync(
				new AssistantChatRequest
			{
				Prompt = "Check repo state.",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "run_command",
						Description = "Run a shell command",
						InputSchemaJson = "{}",
					},
				},
			},
				_ => { });

			Assert.That(response.ToolCallIntent, Is.Not.Null);
			Assert.That(response.ToolCallIntent!.ToolCallId, Is.EqualTo("call_stream"));
			Assert.That(response.ToolCallIntent.ToolName, Is.EqualTo("run_command"));
			Assert.That(response.ToolCallIntent.ArgumentsJson, Is.EqualTo("{\"command\":\"git status --short\"}"));
			Assert.That(response.Message, Is.Empty);
		}

		[Test]
		public void GenerateResponseAsyncWhenFollowUpTimesOutThrowsClearInvalidOperationException()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
				async (_, cancellationToken) =>
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
					return CreateJsonResponse(new JsonObject());
				},
				logService,
				TimeSpan.FromMilliseconds(50));

			var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.GenerateResponseAsync(BuildFollowUpRequest()));

			Assert.That(ex, Is.Not.Null);
			Assert.That(ex!.Message, Does.Contain("follow-up request timed out"));
			Assert.That(logService.Messages, Has.Some.Contains("model-timeout"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenFollowUpContextOverflowsRetriesWithCompactToolResultAsync()
		{
			var requestCount = 0;
			string? retriedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					requestCount++;
					var body = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					if (requestCount == 1)
					{
						return new HttpResponseMessage(HttpStatusCode.BadRequest)
						{
							Content = new StringContent("request exceeds the available context size", Encoding.UTF8, "application/json"),
						};
					}

					retriedBody = body;
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "Finished.",
								},
							},
						},
					});
				},
				logService);

			var request = new AssistantChatRequest
			{
				Prompt = "Summarize the latest results.",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "file_read",
						Description = "Read a file",
						InputSchemaJson = "{}",
					},
				},
				ToolExecutionHistory = new[]
				{
					new AssistantToolExecution
					{
						ToolCallIntent = new AssistantToolCallIntent
						{
							ToolCallId = "call_1",
							ToolName = "file_read",
							ArgumentsJson = "{\"path\":\"C:\\\\temp\\\\a.txt\"}",
						},
						ToolResultText = new string('A', 14000),
						ToolResultRawJson = "{}",
					},
				},
				ToolCallIntent = new AssistantToolCallIntent
				{
					ToolCallId = "call_1",
					ToolName = "file_read",
					ArgumentsJson = "{\"path\":\"C:\\\\temp\\\\a.txt\"}",
				},
				ToolResultText = new string('A', 14000),
				ToolResultRawJson = "{}",
			};

			var response = await service.GenerateResponseAsync(request);

			Assert.That(response.Message, Is.EqualTo("Finished."));
			Assert.That(requestCount, Is.EqualTo(2));
			Assert.That(retriedBody, Does.Contain("[Tool result truncated to fit the model context window."));
			Assert.That(logService.Messages, Has.Some.Contains("retrying with compact tool results"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenFollowUpBuiltIncludesToolHistoryAndToolDefinitionsAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "Finished after the second tool.",
								},
							},
						},
					});
				},
				logService);

			var response = await service.GenerateResponseAsync(BuildFollowUpRequest(toolExecutionCount: 2));

			Assert.That(response.Message, Is.EqualTo("Finished after the second tool."));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			var messages = payload["messages"]!.AsArray();
			var toolMessages = 0;
			foreach (var message in messages)
			{
				if (string.Equals(message?["role"]?.GetValue<string>(), "tool", StringComparison.Ordinal))
				{
					toolMessages++;
				}
			}

			Assert.That(toolMessages, Is.EqualTo(2));
			Assert.That(payload["tools"]!.AsArray().Count, Is.EqualTo(2));
			Assert.That(logService.Messages, Has.Some.Contains("model-follow-up"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenFollowUpHasExtraAvailableToolsOnlyIncludesToolsUsedInTurnAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "Finished.",
								},
							},
						},
					});
				},
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "Summarize the latest results.",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "file_read",
						Description = "Read a file",
						InputSchemaJson = "{}",
					},
					new McpToolDescriptor
					{
						Name = "run_command",
						Description = "Run a shell command",
						InputSchemaJson = "{}",
					},
					new McpToolDescriptor
					{
						Name = "inspect_form",
						Description = "Inspect the active form",
						InputSchemaJson = "{}",
					},
				},
				ToolExecutionHistory = new[]
				{
					new AssistantToolExecution
					{
						ToolCallIntent = new AssistantToolCallIntent
						{
							ToolCallId = "call_1",
							ToolName = "file_read",
							ArgumentsJson = "{\"path\":\"C:\\\\temp\\\\a.txt\"}",
						},
						ToolResultText = "file contents",
						ToolResultRawJson = "{\"text\":\"file contents\"}",
					},
				},
				ToolCallIntent = new AssistantToolCallIntent
				{
					ToolCallId = "call_1",
					ToolName = "file_read",
					ArgumentsJson = "{\"path\":\"C:\\\\temp\\\\a.txt\"}",
				},
				ToolResultText = "file contents",
				ToolResultRawJson = "{\"text\":\"file contents\"}",
			});

			Assert.That(response.Message, Is.EqualTo("Finished."));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			var tools = payload["tools"]!.AsArray();
			Assert.That(tools.Count, Is.EqualTo(1));
			Assert.That(tools[0]!["function"]!["name"]?.GetValue<string>(), Is.EqualTo("file_read"));

			var systemPrompt = payload["messages"]![0]!["content"]!.GetValue<string>();
			Assert.That(systemPrompt, Does.Contain("1 MCP tools are available."));
			Assert.That(systemPrompt, Does.Contain("file_read"));
			Assert.That(systemPrompt, Does.Not.Contain("inspect_form"));
		}

		[Test]
		public async Task GenerateResponseAsyncIncludesFullSchemaMetadataForToolParametersAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "ok",
								},
							},
						},
					});
				},
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "Open the form.",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "open_form",
						Description = "Open a form",
						InputSchemaJson = """
						{
						  "type": "object",
						  "properties": {
						    "form": {
						      "type": "string",
						      "description": "The form identifier to open.",
						      "default": "Shipment"
						    },
						    "mode": {
						      "type": "string",
						      "description": "The launch mode.",
						      "default": "view"
						    }
						  },
						  "required": ["form"]
						}
						""",
					},
				},
			});

			Assert.That(response.Message, Is.EqualTo("ok"));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			var formProperty = payload["tools"]![0]!["function"]!["parameters"]!["properties"]!["form"]!.AsObject();
			var modeProperty = payload["tools"]![0]!["function"]!["parameters"]!["properties"]!["mode"]!.AsObject();

			Assert.That(payload["tools"]![0]!["function"]!["description"]?.GetValue<string>(), Is.EqualTo("Open a form"));
			Assert.That(formProperty["description"]?.GetValue<string>(), Is.EqualTo("The form identifier to open."));
			Assert.That(formProperty["default"]?.GetValue<string>(), Is.EqualTo("Shipment"));
			Assert.That(modeProperty["description"]?.GetValue<string>(), Is.EqualTo("The launch mode."));
			Assert.That(modeProperty["default"]?.GetValue<string>(), Is.EqualTo("view"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenToolIsPinnedRequestsArgumentsWithoutToolCatalogAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "{\"form\":\"Shipment\",\"mode\":\"Summary\"}",
								},
							},
						},
					});
				},
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "inspect the current form",
				PinnedToolName = "inspect_form",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "inspect_form",
						Title = "Inspect Form",
						Description = "Returns field values and controls for the active form in Winzor.",
						InputSchemaJson = """
						{
						  "type": "object",
						  "title": "InspectFormRequest",
						  "properties": {
						    "form": {
						      "type": "string",
						      "description": "Target form name.",
						      "default": "Shipment"
						    },
						    "mode": {
						      "type": "string",
						      "description": "Amount of detail to include.",
						      "enum": ["Summary", "Full", "Debug"],
						      "default": "Summary"
						    }
						  },
						  "required": ["form"]
						}
						""",
					},
					new McpToolDescriptor
					{
						Name = "other_tool",
						Description = "Should never be exposed for a pinned turn.",
						InputSchemaJson = "{}",
					},
				},
			});

			Assert.That(response.Message, Is.Empty);
			Assert.That(response.ToolCallIntent, Is.Not.Null);
			Assert.That(response.ToolCallIntent!.ToolName, Is.EqualTo("inspect_form"));
			Assert.That(response.ToolCallIntent.ArgumentsJson, Is.EqualTo("{\"form\":\"Shipment\",\"mode\":\"Summary\"}"));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			Assert.That(payload["tools"], Is.Null);
			Assert.That(payload["tool_choice"], Is.Null);
			var systemPrompt = payload["messages"]![0]!["content"]!.GetValue<string>();
			Assert.That(systemPrompt, Does.Contain("The selected tool is 'inspect_form'."));
			Assert.That(systemPrompt, Does.Contain("Do not choose, suggest, or mention any other tool."));
			Assert.That(systemPrompt, Does.Contain("InspectFormRequest"));
			Assert.That(capturedBody, Does.Not.Contain("other_tool"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenPinnedToolReturnsFencedJsonCallsToolAsync()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
				(_, _) => Task.FromResult(CreateJsonResponse(new JsonObject
				{
					["choices"] = new JsonArray
					{
						new JsonObject
						{
							["message"] = new JsonObject
							{
								["content"] = "```json\n{\"form\":\"active\",\"action\":\"update_summary\",\"args\":{\"value\":\"how to use Copilot\"}}\n```",
							},
						},
					},
				})),
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "call #invoke_form_action to update summary to \"how to use Copilot\"",
				PinnedToolName = "invoke_form_action",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "invoke_form_action",
						Description = "Invokes a mutation action on the active form.",
						InputSchemaJson = """
						{
						  "type": "object",
						  "properties": {
						    "form": {
						      "type": "string"
						    },
						    "action": {
						      "type": "string"
						    },
						    "args": {
						      "type": "object"
						    }
						  },
						  "required": ["form", "action"]
						}
						""",
					},
				},
			});

			Assert.That(response.Message, Is.Empty);
			Assert.That(response.ToolCallIntent, Is.Not.Null);
			Assert.That(response.ToolCallIntent!.ToolName, Is.EqualTo("invoke_form_action"));
			Assert.That(response.ToolCallIntent.ArgumentsJson, Is.EqualTo("{\"form\":\"active\",\"action\":\"update_summary\",\"args\":{\"value\":\"how to use Copilot\"}}"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenPinnedToolArgumentsMissRequiredFieldsReturnsValidationMessageAsync()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
				(_, _) => Task.FromResult(CreateJsonResponse(new JsonObject
				{
					["choices"] = new JsonArray
					{
						new JsonObject
						{
							["message"] = new JsonObject
							{
								["content"] = "{\"plan\":null}",
							},
						},
					},
				})),
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "change summary in this form",
				PinnedToolName = "invoke_form_action",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "invoke_form_action",
						Description = "Invokes a mutation action on the active form.",
						InputSchemaJson = """
						{
						  "type": "object",
						  "properties": {
						    "action": {
						      "type": "string",
						      "description": "The supported action name."
						    },
						    "plan": {
						      "type": ["object", "null"]
						    }
						  },
						  "required": ["action"]
						}
						""",
					},
				},
			});

			Assert.That(response.ToolCallIntent, Is.Null);
			Assert.That(response.Message, Is.EqualTo("Tool 'invoke_form_action' needs required arguments: action."));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenPinnedToolNeedsClarificationReturnsModelQuestionAsync()
		{
			var logService = new RecordingLogService();
			var service = CreateService(
				(_, _) => Task.FromResult(CreateJsonResponse(new JsonObject
				{
					["choices"] = new JsonArray
					{
						new JsonObject
						{
							["message"] = new JsonObject
							{
								["content"] = "Which action should I run for the current form?",
							},
						},
					},
				})),
				logService);

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "change summary in this form",
				PinnedToolName = "invoke_form_action",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "invoke_form_action",
						Description = "Invokes a mutation action on the active form.",
						InputSchemaJson = "{}",
					},
				},
			});

			Assert.That(response.ToolCallIntent, Is.Null);
			Assert.That(response.Message, Is.EqualTo("Which action should I run for the current form?"));
		}

		[Test]
		public async Task GenerateResponseAsyncWhenToolIsPinnedPreservesLongDescriptionInPinnedPromptAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "Which form should I inspect?",
								},
							},
						},
					});
				},
				logService);

			const string longDescription = "Returns field values and controls for the active form in Winzor. Use the mode to control how much structure is returned.";

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "run",
				PinnedToolName = "inspect_form",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "inspect_form",
						Description = longDescription,
						InputSchemaJson = "{}",
					},
				},
			});

			Assert.That(response.ToolCallIntent, Is.Null);
			Assert.That(response.Message, Is.EqualTo("Which form should I inspect?"));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			var systemPrompt = payload["messages"]![0]!["content"]!.GetValue<string>();
			Assert.That(systemPrompt, Does.Contain(longDescription));
		}

		[Test]
		public async Task BuildPayloadWhenContextWindowExceededPrunesOldestHistoryTurnsAsync()
		{
			string? capturedBody = null;
			var logService = new RecordingLogService();
			var service = CreateService(
				async (requestMessage, _) =>
				{
					capturedBody = await requestMessage.Content!.ReadAsStringAsync(CancellationToken.None);
					return CreateJsonResponse(new JsonObject
					{
						["choices"] = new JsonArray
						{
							new JsonObject
							{
								["message"] = new JsonObject
								{
									["content"] = "answer",
								},
							},
						},
					});
				},
				logService);

			// 10 history turns × 500 chars each = 5000 chars;
			// contextWindow=100 tokens × 4 chars/token = 400 chars budget, forcing pruning.
			var longHistory = new List<AssistantConversationTurn>();
			for (var i = 0; i < 10; i++)
			{
				longHistory.Add(new AssistantConversationTurn
				{
					Role = i % 2 == 0 ? "user" : "assistant",
					Content = new string('X', 500),
				});
			}

			var response = await service.GenerateResponseAsync(new AssistantChatRequest
			{
				Prompt = "What next?",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
					ContextWindow = 100,
				},
				AvailableTools = Array.Empty<McpToolDescriptor>(),
				ConversationHistory = longHistory,
			});

			Assert.That(response.Message, Is.EqualTo("answer"));
			Assert.That(capturedBody, Is.Not.Null);

			var payload = JsonNode.Parse(capturedBody!)!.AsObject();
			var messages = payload["messages"]!.AsArray();

			// Without pruning: 1 system + 10 history + 1 prompt = 12 messages.
			// With pruning: only the 2 most-recent history turns are kept = 4 messages.
			Assert.That(messages.Count, Is.LessThan(12));

			// The current user prompt must be last.
			Assert.That(messages[^1]!["content"]!.GetValue<string>(), Is.EqualTo("What next?"));

			// The two most-recent history turns (turns 8 and 9) must be retained.
			Assert.That(messages[^2]!["content"]!.GetValue<string>(), Is.EqualTo(new string('X', 500)));
			Assert.That(messages[^3]!["content"]!.GetValue<string>(), Is.EqualTo(new string('X', 500)));

			// System prompt must be first.
			Assert.That(messages[0]!["role"]!.GetValue<string>(), Is.EqualTo("system"));
		}

		static AssistantChatService CreateService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, RecordingLogService logService, TimeSpan? timeout = null)
		{
			#pragma warning disable CA2000 // Handler ownership is transferred to HttpClient
			var handler = new DelegatingStubHttpMessageHandler(sendAsync);
#pragma warning restore CA2000
			var httpClient = new HttpClient(handler);
			var factory = new StubHttpClientFactory(httpClient);
			return timeout is null
				? new AssistantChatService(factory, logService, NullLogger<AssistantChatService>.Instance)
				: new AssistantChatService(factory, logService, NullLogger<AssistantChatService>.Instance, timeout.Value);
		}

		static AssistantChatRequest BuildFollowUpRequest(int toolExecutionCount = 1)
		{
			var toolExecutions = new List<AssistantToolExecution>();
			for (var index = 0; index < toolExecutionCount; index++)
			{
				toolExecutions.Add(new AssistantToolExecution
				{
					ToolCallIntent = new AssistantToolCallIntent
					{
						ToolCallId = $"call_{index + 1}",
						ToolName = index == 0 ? "file_read" : "run_command",
						ArgumentsJson = index == 0 ? "{\"path\":\"C:\\\\temp\\\\a.txt\"}" : "{\"command\":\"git status\"}",
					},
					ToolResultText = index == 0 ? "file contents" : "git output",
					ToolResultRawJson = index == 0 ? "{\"text\":\"file contents\"}" : "{\"text\":\"git output\"}",
				});
			}

			return new AssistantChatRequest
			{
				Prompt = "Summarize the latest results.",
				Model = new AssistantModelDescriptor
				{
					Id = "google/gemma-3-4b",
					DisplayName = "Gemma 3 4B",
				},
				AvailableTools = new[]
				{
					new McpToolDescriptor
					{
						Name = "file_read",
						Description = "Read a file",
						InputSchemaJson = "{}",
					},
					new McpToolDescriptor
					{
						Name = "run_command",
						Description = "Run a shell command",
						InputSchemaJson = "{}",
					},
				},
				ToolExecutionHistory = toolExecutions,
				ToolCallIntent = toolExecutions[^1].ToolCallIntent,
				ToolResultText = toolExecutions[^1].ToolResultText,
				ToolResultRawJson = toolExecutions[^1].ToolResultRawJson,
			};
		}

		static HttpResponseMessage CreateJsonResponse(JsonObject body)
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
			};
		}

		static HttpResponseMessage CreateStreamingResponse(params string[] events)
		{
			var builder = new StringBuilder();
			foreach (var @event in events)
			{
				builder.Append("data: ");
				builder.Append(@event);
				builder.Append("\n\n");
			}

			builder.Append("data: [DONE]\n\n");
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(builder.ToString(), Encoding.UTF8, "text/event-stream"),
			};
		}

		sealed class StubHttpClientFactory : IHttpClientFactory
		{
			readonly HttpClient httpClient;

			public StubHttpClientFactory(HttpClient httpClient)
			{
				this.httpClient = httpClient;
			}

			public HttpClient CreateClient(string name)
			{
				return httpClient;
			}
		}

		sealed class DelegatingStubHttpMessageHandler : HttpMessageHandler
		{
			readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync;

			public DelegatingStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
			{
				this.sendAsync = sendAsync;
			}

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				return sendAsync(request, cancellationToken);
			}
		}

		sealed class RecordingLogService : IMcpLogService
		{
			public List<string> Messages { get; } = new List<string>();

			public void Log(string category, string message)
			{
				Messages.Add($"{category}:{message}");
			}
		}
	}
}
