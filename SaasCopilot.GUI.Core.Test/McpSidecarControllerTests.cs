using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	[TestFixture]
	[SupportedOSPlatform("windows10.0.17763.0")]
	public class McpSidecarControllerTests
	{
		[Test]
		public async Task SubmitPromptAsyncWhenPromptContainsOnlyToolMentionReturnsValidationErrorWithoutCallingModelAsync()
		{
			var tool = new McpToolDescriptor
			{
				Name = "get_staff_info",
				Description = "Get staff information",
				InputSchemaJson = "{}",
			};
			var transcriptStore = new RecordingTranscriptStore();
			var chatService = new RecordingAssistantChatService();
			var controller = CreateController(tool, transcriptStore, chatService);

			var submitted = await controller.SubmitPromptAsync("#get_staff_info");

			Assert.That(submitted, Is.False);
			Assert.That(chatService.CallCount, Is.EqualTo(0));
			Assert.That(controller.ConnectionState, Is.EqualTo(McpConnectionState.Disconnected));
			Assert.That(controller.LastResultText, Does.Contain("needs more input"));
			Assert.That(controller.TranscriptEntries, Has.Some.Matches<McpTranscriptEntry>(entry =>
				string.Equals(entry.Kind, "error", StringComparison.Ordinal)
				&& string.Equals(entry.Title, "Prompt incomplete", StringComparison.Ordinal)));
			Assert.That(transcriptStore.Entries, Has.Count.EqualTo(1));
		}

		[Test]
		public async Task SubmitPromptAsyncWhenLeadingToolMentionIsUnknownReturnsUnavailableToolErrorAsync()
		{
			var transcriptStore = new RecordingTranscriptStore();
			var chatService = new RecordingAssistantChatService();
			var controller = CreateController(Array.Empty<McpToolDescriptor>(), transcriptStore, chatService);

			var submitted = await controller.SubmitPromptAsync("#get_staff_info");

			Assert.That(submitted, Is.False);
			Assert.That(chatService.CallCount, Is.EqualTo(0));
			Assert.That(controller.LastResultText, Does.Contain("not available"));
			Assert.That(controller.TranscriptEntries, Has.Some.Matches<McpTranscriptEntry>(entry =>
				string.Equals(entry.Kind, "error", StringComparison.Ordinal)
				&& entry.Message.Contains("get_staff_info", StringComparison.Ordinal)));
		}

		[Test]
		public async Task SubmitPromptAsyncWhenCanceledRestoresReadyStateAsync()
		{
			var transcriptStore = new RecordingTranscriptStore();
			var chatService = new CancelAwareAssistantChatService();
			var controller = CreateController(Array.Empty<McpToolDescriptor>(), transcriptStore, chatService);

			var submitTask = controller.SubmitPromptAsync("inspect current form");
			await chatService.WaitForCallAsync();
			controller.CancelCurrentOperation();
			var submitted = await submitTask;

			Assert.That(submitted, Is.False);
			Assert.That(chatService.WasCanceled, Is.True);
			Assert.That(controller.ConnectionState, Is.EqualTo(McpConnectionState.Disconnected));
			Assert.That(controller.LastResultText, Is.EqualTo("Canceled the current assistant request."));
			Assert.That(controller.TranscriptEntries, Has.Some.Matches<McpTranscriptEntry>(entry =>
				string.Equals(entry.Kind, "system", StringComparison.Ordinal)
				&& string.Equals(entry.Title, "Request canceled", StringComparison.Ordinal)));
		}

		[Test]
		public async Task SubmitPromptAsyncWhenResponseStreamsUsesSingleAssistantTranscriptEntryAsync()
		{
			var transcriptStore = new RecordingTranscriptStore();
			var chatService = new StreamingAssistantChatService();
			var controller = CreateController(Array.Empty<McpToolDescriptor>(), transcriptStore, chatService);

			var submitted = await controller.SubmitPromptAsync("inspect current form");

			Assert.That(submitted, Is.True);
			Assert.That(controller.LastResultText, Is.EqualTo("Hello world"));
			Assert.That(controller.TranscriptEntries, Has.Count.EqualTo(2));
			Assert.That(controller.TranscriptEntries, Has.Exactly(1).Matches<McpTranscriptEntry>(entry =>
				string.Equals(entry.Kind, "assistant", StringComparison.Ordinal)
				&& string.Equals(entry.Message, "Hello world", StringComparison.Ordinal)));
			Assert.That(transcriptStore.Entries, Has.Count.EqualTo(2));
			Assert.That(chatService.ObservedDeltaCallback, Is.True);
		}

		static McpSidecarController CreateController(McpToolDescriptor tool, RecordingTranscriptStore transcriptStore, IAssistantChatService chatService)
		{
			return CreateController(new[] { tool }, transcriptStore, chatService);
		}

		static McpSidecarController CreateController(IReadOnlyList<McpToolDescriptor> tools, RecordingTranscriptStore transcriptStore, IAssistantChatService chatService)
		{
			return new McpSidecarController(
				new StubEndpointResolver(),
				new StubConfigurationStore(),
				new StubModelSelectionService(),
				chatService,
				transcriptStore,
				new StubToolCatalog(tools),
				new StubToolInvoker(),
				new StubApprovalService(),
				new RecordingLogService(),
				NullLogger<McpSidecarController>.Instance,
				() => new Uri("https://cw.local/app"));
		}

		sealed class StubEndpointResolver : IMcpEndpointResolver
		{
			public McpEndpointResolution Resolve(Uri? activeApplicationUri, string? endpointOverride)
			{
				return McpEndpointResolution.Success(new Uri("https://cw.local/mcp"));
			}
		}

		sealed class StubConfigurationStore : IMcpConfigurationStore
		{
			public McpSidecarConfiguration Load()
			{
				return new McpSidecarConfiguration();
			}

			public void Save(McpSidecarConfiguration configuration)
			{
			}
		}

		sealed class StubModelSelectionService : IAssistantModelSelectionService
		{
			static readonly AssistantModelDescriptor Model = new AssistantModelDescriptor
			{
				Id = "google/gemma-3-4b",
				DisplayName = "Gemma 3 4B",
			};

			public IReadOnlyList<AssistantModelDescriptor> GetAvailableModels()
			{
				return new[] { Model };
			}

			public Task<IReadOnlyList<AssistantModelDescriptor>> RefreshAvailableModelsAsync(CancellationToken cancellationToken = default)
			{
				return Task.FromResult<IReadOnlyList<AssistantModelDescriptor>>(new[] { Model });
			}

			public AssistantModelDescriptor? GetSelectedModel()
			{
				return Model;
			}

			public AssistantModelDescriptor? SetSelectedModel(string? modelId)
			{
				return Model;
			}
		}

		sealed class RecordingAssistantChatService : IAssistantChatService
		{
			public int CallCount { get; private set; }

			public Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default)
			{
				CallCount++;
				return Task.FromResult(new AssistantChatResponse { Message = "ok" });
			}
		}

		sealed class StreamingAssistantChatService : IAssistantChatService
		{
			public bool ObservedDeltaCallback { get; private set; }

			public Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default)
			{
				ObservedDeltaCallback = onMessageDelta is not null;
				onMessageDelta?.Invoke("Hello");
				onMessageDelta?.Invoke(" world");
				return Task.FromResult(new AssistantChatResponse { Message = "Hello world" });
			}
		}

		sealed class CancelAwareAssistantChatService : IAssistantChatService
		{
			readonly TaskCompletionSource started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			public bool WasCanceled { get; private set; }

			public async Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default)
			{
				started.TrySetResult();
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					WasCanceled = true;
					throw;
				}

				return new AssistantChatResponse { Message = "ok" };
			}

			[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Test helper waits on a TaskCompletionSource signal raised by the stub chat service.")]
			public async Task WaitForCallAsync()
			{
				await started.Task.ConfigureAwait(false);
			}
		}

		sealed class RecordingTranscriptStore : IMcpTranscriptStore
		{
			public List<McpTranscriptEntry> Entries { get; } = new List<McpTranscriptEntry>();

			public IReadOnlyList<McpTranscriptEntry> LoadRecent(int maxEntries)
			{
				return Entries;
			}

			public void Append(McpTranscriptEntry entry)
			{
				Entries.Add(entry);
			}

			public void Clear()
			{
				Entries.Clear();
			}
		}

		sealed class StubToolCatalog : IMcpToolCatalog
		{
			readonly IReadOnlyList<McpToolDescriptor> tools;

			public StubToolCatalog(IReadOnlyList<McpToolDescriptor> tools)
			{
				this.tools = tools;
			}

			public IReadOnlyList<McpToolDescriptor> GetBuiltInTools()
			{
				return tools;
			}

			public Task<IReadOnlyList<McpToolDescriptor>> RefreshAsync(Uri endpoint, CancellationToken cancellationToken = default)
			{
				return Task.FromResult(tools);
			}
		}

		sealed class StubToolInvoker : IMcpToolInvoker
		{
			public Task<McpToolCallResult> InvokeAsync(Uri endpoint, McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default)
			{
				throw new NotSupportedException();
			}
		}

		sealed class StubApprovalService : IMcpApprovalService
		{
			public bool RequiresApproval(Uri endpoint, McpToolDescriptor tool)
			{
				return false;
			}

			public void SaveTrust(Uri endpoint, McpToolDescriptor tool)
			{
			}

			public void RecordDecision(Uri endpoint, McpToolDescriptor tool, bool approved, bool remembered)
			{
			}
		}

		sealed class RecordingLogService : IMcpLogService
		{
			public void Log(string category, string message)
			{
			}
		}
	}
}