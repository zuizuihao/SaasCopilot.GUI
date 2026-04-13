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
using Microsoft.Extensions.Logging;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The controller disposes prompt cancellation sources at the end of each assistant operation.")]
	public sealed class McpSidecarController
	{
		const int DefaultMaxEntries = 50;
		const int MaxToolCallsPerTurn = 2;
		const int MinPanelWidth = 320;

		static readonly Uri BuiltinEndpoint = new Uri("builtin://local");

		readonly IMcpEndpointResolver endpointResolver;
		readonly IMcpConfigurationStore configurationStore;
		readonly IAssistantModelSelectionService modelSelectionService;
		readonly IAssistantChatService assistantChatService;
		readonly IMcpTranscriptStore transcriptStore;
		readonly IMcpToolCatalog toolCatalog;
		readonly IMcpToolInvoker toolInvoker;
		readonly IMcpApprovalService approvalService;
		readonly IMcpLogService logService;
		readonly ILogger<McpSidecarController> logger;
		readonly Func<Uri?> activeApplicationUriProvider;
		readonly IMcpHubConnection hubConnection;
		readonly SynchronizationContext? synchronizationContext;
		readonly List<McpTranscriptEntry> transcriptEntries;
		CancellationTokenSource? currentPromptOperationCts;
		Uri? lastAutoConnectedEndpoint;

		public McpSidecarController(
			IMcpEndpointResolver endpointResolver,
			IMcpConfigurationStore configurationStore,
			IAssistantModelSelectionService modelSelectionService,
			IAssistantChatService assistantChatService,
			IMcpTranscriptStore transcriptStore,
			IMcpToolCatalog toolCatalog,
			IMcpToolInvoker toolInvoker,
			IMcpApprovalService approvalService,
			IMcpLogService logService,
			ILogger<McpSidecarController> logger,
			Func<Uri?> activeApplicationUriProvider,
			IMcpHubConnection hubConnection)
		{
			ArgumentNullException.ThrowIfNull(endpointResolver);
			ArgumentNullException.ThrowIfNull(configurationStore);
			ArgumentNullException.ThrowIfNull(modelSelectionService);
			ArgumentNullException.ThrowIfNull(assistantChatService);
			ArgumentNullException.ThrowIfNull(transcriptStore);
			ArgumentNullException.ThrowIfNull(toolCatalog);
			ArgumentNullException.ThrowIfNull(toolInvoker);
			ArgumentNullException.ThrowIfNull(approvalService);
			ArgumentNullException.ThrowIfNull(logService);
			ArgumentNullException.ThrowIfNull(logger);
			ArgumentNullException.ThrowIfNull(activeApplicationUriProvider);
			ArgumentNullException.ThrowIfNull(hubConnection);

			this.endpointResolver = endpointResolver;
			this.configurationStore = configurationStore;
			this.modelSelectionService = modelSelectionService;
			this.assistantChatService = assistantChatService;
			this.transcriptStore = transcriptStore;
			this.toolCatalog = toolCatalog;
			this.toolInvoker = toolInvoker;
			this.approvalService = approvalService;
			this.logService = logService;
			this.logger = logger;
			this.activeApplicationUriProvider = activeApplicationUriProvider;
			this.hubConnection = hubConnection;
			this.synchronizationContext = SynchronizationContext.Current;
			hubConnection.ActionReceived += OnHubActionReceived;

			var configuration = configurationStore.Load();
			IsVisible = configuration.IsVisible;
			AutoConnectOnStartup = configuration.AutoConnectOnStartup;
			PanelWidth = Math.Max(MinPanelWidth, configuration.PanelWidth);
			EndpointOverride = configuration.EndpointOverride;
			transcriptEntries = transcriptStore.LoadRecent(DefaultMaxEntries).ToList();
			ToolArgumentsJson = "{}";
			AvailableTools = toolCatalog.GetBuiltInTools();
			AvailableModels = modelSelectionService.GetAvailableModels();
			SelectedModel = modelSelectionService.GetSelectedModel();
			RefreshEndpoint();
		}

		public event EventHandler? StateChanged;

		public bool IsVisible { get; private set; }

		public bool AutoConnectOnStartup { get; private set; }

		public int PanelWidth { get; private set; }

		public string? EndpointOverride { get; private set; }

		public Uri? ResolvedEndpoint { get; private set; }

		public string? ResolutionFailureReason { get; private set; }

		public McpConnectionState ConnectionState { get; private set; }

		public string StatusText { get; private set; } = "Disabled";

		public string? ActivityText { get; private set; }

		public IReadOnlyList<AssistantModelDescriptor> AvailableModels { get; private set; }

		public AssistantModelDescriptor? SelectedModel { get; private set; }

		public IReadOnlyList<McpTranscriptEntry> TranscriptEntries => transcriptEntries;

		public IReadOnlyList<McpToolDescriptor> AvailableTools { get; private set; }

		public string? SelectedToolName { get; private set; }

		public string ToolArgumentsJson { get; private set; }

		public McpApprovalRequest? PendingApprovalRequest { get; private set; }

		public string? LastResultText { get; private set; }

		public McpAssistantMode AssistantMode { get; private set; } = McpAssistantMode.Act;

		public bool CanCancelCurrentOperation => currentPromptOperationCts is not null && !currentPromptOperationCts.IsCancellationRequested;

		public void SetVisible(bool visible)
		{
			if (IsVisible == visible)
			{
				return;
			}

			IsVisible = visible;
			if (!visible)
			{
				PendingApprovalRequest = null;
			}

			ConnectionState = visible ? ConnectionState : McpConnectionState.Disabled;
			StatusText = visible ? StatusText : "Disabled";
			if (!visible)
			{
				ActivityText = null;
				_ = hubConnection.StopAsync();
			}
			Persist();
			NotifyStateChanged();
		}

		public void SetPanelWidth(int panelWidth)
		{
			var clampedWidth = Math.Max(MinPanelWidth, panelWidth);
			if (PanelWidth == clampedWidth)
			{
				return;
			}

			PanelWidth = clampedWidth;
			Persist();
		}

		public void SetAssistantMode(McpAssistantMode mode)
		{
			if (AssistantMode == mode)
			{
				return;
			}

			AssistantMode = mode;
			var modeMessage = mode == McpAssistantMode.Chat
				? "Switched to Chat mode. MCP tools are disabled."
				: "Switched to Act mode. Use #toolName to invoke an MCP tool.";
			AppendTranscript("model", modeMessage);
			NotifyStateChanged();
		}

		public void SetEndpointOverride(string? endpointOverride)
		{
			UpdateConnectionPreferences(endpointOverride, AutoConnectOnStartup);
		}

		public void UpdateConnectionPreferences(string? endpointOverride, bool autoConnectOnStartup)
		{
			var normalizedEndpointOverride = string.IsNullOrWhiteSpace(endpointOverride) ? null : endpointOverride.Trim();
			var endpointChanged = !string.Equals(EndpointOverride, normalizedEndpointOverride, StringComparison.Ordinal);
			var autoConnectChanged = AutoConnectOnStartup != autoConnectOnStartup;

			EndpointOverride = normalizedEndpointOverride;
			AutoConnectOnStartup = autoConnectOnStartup;
			if (endpointChanged)
			{
				lastAutoConnectedEndpoint = null;
			}

			Persist();

			if (endpointChanged || autoConnectChanged)
			{
				var endpointText = EndpointOverride is null ? "Using resolved application endpoint." : $"Using override '{EndpointOverride}'.";
				var startupText = AutoConnectOnStartup ? "Startup connection is enabled." : "Startup connection is disabled.";
				AppendTranscript("config", "Configuration updated", $"{endpointText} {startupText}");
			}

			RefreshEndpoint();
		}

		public void RefreshEndpoint()
		{
			PendingApprovalRequest = null;
			ActivityText = null;
			var resolution = endpointResolver.Resolve(activeApplicationUriProvider(), EndpointOverride);
			var endpointChanged = !UriEquals(ResolvedEndpoint, resolution.Endpoint);
			if (endpointChanged)
			{
				AvailableTools = toolCatalog.GetBuiltInTools();
				SelectedToolName = null;
				_ = hubConnection.StopAsync();
			}
			ResolvedEndpoint = resolution.Endpoint;
			ResolutionFailureReason = resolution.FailureReason;
			ConnectionState = !IsVisible
				? McpConnectionState.Disabled
				: resolution.Succeeded
					? McpConnectionState.Disconnected
					: McpConnectionState.EndpointUnresolved;

			StatusText = ConnectionState switch
			{
				McpConnectionState.Disabled => "Disabled",
				McpConnectionState.EndpointUnresolved => "Endpoint unresolved",
				McpConnectionState.Disconnected => "Disconnected",
				McpConnectionState.Connecting => "Connecting",
				McpConnectionState.GeneratingResponse => "Generating response",
				McpConnectionState.Connected => "Connected",
				McpConnectionState.DiscoveryFailed => "Discovery failed",
				McpConnectionState.ModelResponseFailed => "Model response failed",
				McpConnectionState.ToolInvocationFailed => "Tool invocation failed",
				McpConnectionState.ApprovalRequired => "Approval required",
				_ => "Unknown",
			};

			logService.Log("resolution", resolution.Succeeded
				? $"Resolved endpoint '{ResolvedEndpoint}'."
				: $"Failed to resolve endpoint. {ResolutionFailureReason}");

			NotifyStateChanged();
		}

		public async Task TryAutoConnectAsync()
		{
			if (!AutoConnectOnStartup)
			{
				return;
			}

			RefreshEndpoint();
			if (ResolvedEndpoint is null
				|| ConnectionState == McpConnectionState.Connecting
				|| ConnectionState == McpConnectionState.GeneratingResponse
				|| UriEquals(lastAutoConnectedEndpoint, ResolvedEndpoint))
			{
				return;
			}

			lastAutoConnectedEndpoint = ResolvedEndpoint;
			ActivityText = $"Connecting to MCP on startup for '{ResolvedEndpoint}'.";
			await ConnectAsync();
		}

		public async Task ConnectAsync()
		{
			RefreshEndpoint();
			if (ResolvedEndpoint is null)
			{
				AppendTranscript("error", ResolutionFailureReason ?? "No MCP endpoint could be resolved.");
				return;
			}

			ConnectionState = McpConnectionState.Connecting;
			StatusText = "Connecting";
			ActivityText = $"Connecting to '{ResolvedEndpoint}' and discovering tools.";
			NotifyStateChanged();

			try
			{
				AvailableTools = await toolCatalog.RefreshAsync(ResolvedEndpoint);
				if (AvailableTools.Count == 0)
				{
					SelectedToolName = null;
				}
				else
				{
					var selectedToolStillExists = false;
					foreach (var tool in AvailableTools)
					{
						if (string.Equals(tool.Name, SelectedToolName, StringComparison.Ordinal))
						{
							selectedToolStillExists = true;
							break;
						}
					}

					if (!selectedToolStillExists)
					{
						SelectedToolName = AvailableTools[0].Name;
					}
				}
				ConnectionState = McpConnectionState.Connected;
				StatusText = "Connected";
				AppendTranscript("system", "Connected", $"Connected to '{ResolvedEndpoint}' and discovered {AvailableTools.Count} tools.");
				logger.LogInformation("Connected to MCP endpoint {endpoint} with {toolCount} tools", ResolvedEndpoint, AvailableTools.Count);
				logService.Log("discovery", $"Discovered {AvailableTools.Count} tools from '{ResolvedEndpoint}'.");
				_ = StartHubAsync(ResolvedEndpoint);
			}
			catch (HttpRequestException ex)
			{
				HandleConnectFailure(ex);
			}
			catch (IOException ex)
			{
				HandleConnectFailure(ex);
			}
			catch (InvalidOperationException ex)
			{
				HandleConnectFailure(ex);
			}
			catch (JsonException ex)
			{
				HandleConnectFailure(ex);
			}
			catch (NotSupportedException ex)
			{
				HandleConnectFailure(ex);
			}

			NotifyStateChanged();
		}

		public async Task RefreshAvailableModelsAsync()
		{
			var refreshedModels = await modelSelectionService.RefreshAvailableModelsAsync().ConfigureAwait(false);
			AvailableModels = refreshedModels;
			SelectedModel = modelSelectionService.SetSelectedModel(SelectedModel?.Id);
			NotifyStateChanged();
		}

		public Task RefreshToolsAsync()
		{
			return ConnectAsync();
		}

		public void SelectModel(string? modelId)
		{
			var previousModelId = SelectedModel?.Id;
			SelectedModel = modelSelectionService.SetSelectedModel(modelId);

			if (!string.Equals(previousModelId, SelectedModel?.Id, StringComparison.Ordinal))
			{
				AppendTranscript("model", "Model changed", $"Selected model '{SelectedModel?.DisplayName}'.");
				logService.Log("model", $"Selected model '{SelectedModel?.Id}'.");
				return;
			}

			NotifyStateChanged();
		}

		public void SelectTool(string? toolName)
		{
			SelectedToolName = toolName;
			NotifyStateChanged();
		}

		public void SetToolArgumentsJson(string? toolArgumentsJson)
		{
			ToolArgumentsJson = string.IsNullOrWhiteSpace(toolArgumentsJson) ? "{}" : toolArgumentsJson;
		}

		public async Task<bool> SubmitPromptAsync(string? prompt)
		{
			var normalizedPrompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
			if (normalizedPrompt is null || SelectedModel is null)
			{
				return false;
			}

			if (!TryPreparePromptSubmission(normalizedPrompt, out var preparation))
			{
				return false;
			}

			// Capture history before appending the current user turn so it is not included as prior context.
			var conversationHistory = BuildConversationHistory();
			AppendTranscript("user", normalizedPrompt);
			ConnectionState = McpConnectionState.GeneratingResponse;
			StatusText = $"Generating with {SelectedModel.DisplayName}";
			ActivityText = $"Sending the request to {SelectedModel.DisplayName}.";
			NotifyStateChanged();
			using var promptOperationCts = BeginPromptOperation();
			var cancellationToken = promptOperationCts.Token;
			var streamedResponse = new StreamedAssistantResponseState();

			try
			{
				if (preparation.DirectToolIntent is not null)
				{
					var directToolResult = await InvokeToolIntentAsync(preparation.DirectToolIntent, normalizedPrompt, cancellationToken);
					if (directToolResult is not null)
					{
						await CompleteToolBackedTurnAsync(normalizedPrompt, conversationHistory, AppendToolExecution(Array.Empty<AssistantToolExecution>(), preparation.DirectToolIntent, directToolResult), cancellationToken);
					}

					return true;
				}

				// Tools are invoked only when the user explicitly mentions one with #toolName in Act mode.
				// Chat mode never invokes tools, even when a #toolName mention is present.
				var toolsForRequest = preparation.MentionedTool is not null && AssistantMode != McpAssistantMode.Chat
					? (IReadOnlyList<McpToolDescriptor>)new[] { preparation.MentionedTool }
					: Array.Empty<McpToolDescriptor>();

				var response = await assistantChatService.GenerateResponseAsync(
					new AssistantChatRequest
					{
						Prompt = preparation.PromptForModel,
						Model = SelectedModel,
						Endpoint = ResolvedEndpoint,
						AvailableTools = toolsForRequest,
						PinnedToolName = preparation.MentionedTool?.Name,
						ConversationHistory = conversationHistory,
					},
					preparation.MentionedTool is null ? CreateAssistantStreamHandler(streamedResponse) : null,
					cancellationToken);

				FinalizeAssistantResponse(streamedResponse, response.Message);

				if (response.ToolCallIntent is not null && AssistantMode != McpAssistantMode.Chat)
				{
					var toolResult = await InvokeToolIntentAsync(response.ToolCallIntent, normalizedPrompt, cancellationToken);
					if (toolResult is not null)
					{
						await CompleteToolBackedTurnAsync(normalizedPrompt, conversationHistory, AppendToolExecution(Array.Empty<AssistantToolExecution>(), response.ToolCallIntent, toolResult), cancellationToken);
					}

					return true;
				}

				RestoreReadyState();
				return true;
			}
			catch (HttpRequestException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			catch (IOException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			catch (InvalidOperationException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			catch (ArgumentException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptCanceled();
			}
			catch (JsonException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			catch (NotSupportedException ex)
			{
				DiscardEmptyAssistantResponse(streamedResponse);
				HandlePromptFailure(ex);
			}
			finally
			{
				EndPromptOperation(promptOperationCts);
			}

			return false;
		}

		bool TryPreparePromptSubmission(string normalizedPrompt, out PromptSubmissionPreparation preparation)
		{
			// Extract any #toolName mention out-of-band before transcript or model work begins.
			// The transcript records the original user text (including the mention token, just
			// like VS Code does), but the model receives a clean prompt with the token stripped.
			var leadingToolMention = TryExtractLeadingToolMentionName(normalizedPrompt, out var toolMentionName)
				? toolMentionName
				: null;
			var mentionedTool = ExtractMentionedTool(normalizedPrompt, AvailableTools);
			if (leadingToolMention is not null
				&& mentionedTool is null
				&& AssistantMode != McpAssistantMode.Chat)
			{
				HandlePromptValidationFailure(BuildUnavailableToolMessage(leadingToolMention));
				preparation = PromptSubmissionPreparation.Empty;
				return false;
			}

			var promptForModel = mentionedTool is not null && AssistantMode != McpAssistantMode.Chat
				? McpDirectToolIntentParser.StripToolMention(normalizedPrompt, mentionedTool.Name)
				: normalizedPrompt;
			var directToolIntent = mentionedTool is not null && AssistantMode != McpAssistantMode.Chat
				&& McpDirectToolIntentParser.TryBuildIntent(normalizedPrompt, mentionedTool, out var parsedToolIntent)
					? parsedToolIntent
					: null;

			if (mentionedTool is not null
				&& AssistantMode != McpAssistantMode.Chat
				&& directToolIntent is null
				&& string.IsNullOrWhiteSpace(promptForModel))
			{
				HandlePromptValidationFailure($"Tool '{mentionedTool.Name}' needs more input. Add a short request after #{mentionedTool.Name}, or provide JSON arguments with #{mentionedTool.Name} {{ ... }}.");
				preparation = PromptSubmissionPreparation.Empty;
				return false;
			}

			preparation = new PromptSubmissionPreparation(promptForModel, mentionedTool, directToolIntent);
			return true;
		}

		static McpToolDescriptor? ExtractMentionedTool(string prompt, IReadOnlyList<McpToolDescriptor> tools)
		{
			if (!TryExtractToolMentionName(prompt, out var mention))
			{
				return null;
			}
			foreach (var tool in tools)
			{
				if (string.Equals(tool.Name, mention, StringComparison.OrdinalIgnoreCase))
				{
					return tool;
				}
			}

			return null;
		}

		static bool TryExtractLeadingToolMentionName(string prompt, out string? toolName)
		{
			toolName = null;
			if (!prompt.StartsWith("#", StringComparison.Ordinal))
			{
				return false;
			}

			return TryExtractToolMentionName(prompt, out toolName);
		}

		static bool TryExtractToolMentionName(string prompt, out string? toolName)
		{
			toolName = null;
			var hashIndex = prompt.IndexOf("#", StringComparison.Ordinal);
			if (hashIndex < 0)
			{
				return false;
			}

			var start = hashIndex + 1;
			var end = start;
			while (end < prompt.Length && (char.IsLetterOrDigit(prompt[end]) || prompt[end] == '_' || prompt[end] == '-'))
			{
				end++;
			}

			if (end == start)
			{
				return false;
			}

			toolName = prompt[start..end];
			return true;
		}

		public async Task ApprovePendingToolInvocationAsync(bool rememberApproval)
		{
			if (PendingApprovalRequest is null)
			{
				return;
			}

			var approvalRequest = PendingApprovalRequest;
			PendingApprovalRequest = null;

			approvalService.RecordDecision(approvalRequest.Endpoint, approvalRequest.Tool, approved: true, remembered: rememberApproval);
			if (rememberApproval)
			{
				approvalService.SaveTrust(approvalRequest.Endpoint, approvalRequest.Tool);
			}

			AppendTranscript("approval", rememberApproval
				? "Approved and trusted the tool call for future requests."
				: "Approved the tool call for this request only.");
			using var promptOperationCts = BeginPromptOperation();
			var cancellationToken = promptOperationCts.Token;

			try
			{
				var toolResult = await InvokeToolAsync(approvalRequest, cancellationToken);
				if (toolResult is not null && approvalRequest.ToolCallIntent is not null && !string.IsNullOrWhiteSpace(approvalRequest.OriginalPrompt))
				{
					await CompleteToolBackedTurnAsync(
						approvalRequest.OriginalPrompt,
						approvalRequest.ConversationHistory,
						AppendToolExecution(approvalRequest.ToolExecutionHistory, approvalRequest.ToolCallIntent, toolResult),
						cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				HandlePromptCanceled();
			}
			finally
			{
				EndPromptOperation(promptOperationCts);
			}
		}

		public void CancelCurrentOperation()
		{
			if (!CanCancelCurrentOperation)
			{
				return;
			}

			ActivityText = "Canceling the current assistant request...";
			logService.Log("model-cancel", "User requested cancellation for the current assistant operation.");
			currentPromptOperationCts!.Cancel();
			NotifyStateChanged();
		}

		public void DeclinePendingToolInvocation()
		{
			if (PendingApprovalRequest is null)
			{
				return;
			}

			var approvalRequest = PendingApprovalRequest;
			PendingApprovalRequest = null;
			approvalService.RecordDecision(approvalRequest.Endpoint, approvalRequest.Tool, approved: false, remembered: false);
			RestoreReadyState();
			AppendTranscript("approval", "Declined", $"Declined invocation of '{approvalRequest.Tool.Name}'.");
		}

		public async Task InvokeSelectedToolAsync()
		{
			await InvokeSelectedToolAsync(null, null);
		}

		async Task InvokeSelectedToolAsync(string? originalPrompt, AssistantToolCallIntent? toolCallIntent)
		{
			McpToolDescriptor? tool = null;
			foreach (var candidate in AvailableTools)
			{
				if (string.Equals(candidate.Name, SelectedToolName, StringComparison.Ordinal))
				{
					tool = candidate;
					break;
				}
			}

			if (tool is null)
			{
				AppendTranscript("error", "Tool call blocked", "Select a tool before invoking it.");
				return;
			}

			var effectiveEndpoint = tool.Source == McpToolSource.BuiltIn ? BuiltinEndpoint : ResolvedEndpoint;
			if (effectiveEndpoint is null)
			{
				AppendTranscript("error", "Tool call blocked", "Resolve an MCP endpoint before invoking a tool.");
				return;
			}

			var approvalRequest = new McpApprovalRequest
			{
				Endpoint = effectiveEndpoint,
				Tool = tool,
				ArgumentsJson = ToolArgumentsJson,
				OriginalPrompt = originalPrompt,
				ToolCallIntent = toolCallIntent,
			};

			if (approvalService.RequiresApproval(effectiveEndpoint, tool))
			{
				PendingApprovalRequest = approvalRequest;
				ConnectionState = McpConnectionState.ApprovalRequired;
				StatusText = "Approval required";
				AppendTranscript("approval", "Confirm tool call", $"'{tool.Name}' wants to run on {effectiveEndpoint.Authority}. Review the arguments and confirm to continue.", approvalRequest.ArgumentsJson);
				NotifyStateChanged();
				return;
			}

			await InvokeToolAsync(approvalRequest);
		}

		async Task<McpToolCallResult?> InvokeToolIntentAsync(AssistantToolCallIntent toolCallIntent, string originalPrompt, CancellationToken cancellationToken, IReadOnlyList<AssistantToolExecution>? toolExecutionHistory = null)
		{
			SelectTool(toolCallIntent.ToolName);
			SetToolArgumentsJson(toolCallIntent.ArgumentsJson);
			return await InvokeSelectedToolWithResultAsync(originalPrompt, toolCallIntent, toolExecutionHistory ?? Array.Empty<AssistantToolExecution>(), cancellationToken);
		}

		async Task<McpToolCallResult?> InvokeSelectedToolWithResultAsync(string? originalPrompt, AssistantToolCallIntent? toolCallIntent, IReadOnlyList<AssistantToolExecution> toolExecutionHistory, CancellationToken cancellationToken)
		{
			McpToolDescriptor? tool = null;
			foreach (var candidate in AvailableTools)
			{
				if (string.Equals(candidate.Name, SelectedToolName, StringComparison.Ordinal))
				{
					tool = candidate;
					break;
				}
			}

			if (tool is null)
			{
				AppendTranscript("error", "Tool call blocked", "Select a tool before invoking it.");
				return null;
			}

			var effectiveEndpoint = tool.Source == McpToolSource.BuiltIn ? BuiltinEndpoint : ResolvedEndpoint;
			if (effectiveEndpoint is null)
			{
				AppendTranscript("error", "Tool call blocked", "Resolve an MCP endpoint before invoking a tool.");
				return null;
			}

			var approvalRequest = new McpApprovalRequest
			{
				Endpoint = effectiveEndpoint,
				Tool = tool,
				ArgumentsJson = ToolArgumentsJson,
				OriginalPrompt = originalPrompt,
				ToolCallIntent = toolCallIntent,
				ToolExecutionHistory = toolExecutionHistory,
				ConversationHistory = toolCallIntent is not null && originalPrompt is not null
					? BuildConversationHistory()
					: Array.Empty<AssistantConversationTurn>(),
			};

			if (approvalService.RequiresApproval(effectiveEndpoint, tool))
			{
				PendingApprovalRequest = approvalRequest;
				ConnectionState = McpConnectionState.ApprovalRequired;
				StatusText = "Approval required";
				AppendTranscript("approval", "Confirm tool call", $"'{tool.Name}' wants to run on {effectiveEndpoint.Authority}. Review the arguments and confirm to continue.", approvalRequest.ArgumentsJson);
				NotifyStateChanged();
				return null;
			}

			return await InvokeToolAsync(approvalRequest, cancellationToken);
		}

		async Task<McpToolCallResult?> InvokeToolAsync(McpApprovalRequest approvalRequest, CancellationToken cancellationToken = default)
		{
			ConnectionState = McpConnectionState.GeneratingResponse;
			StatusText = $"Running {approvalRequest.Tool.Name}";
			ActivityText = $"Running '{approvalRequest.Tool.Name}' on {approvalRequest.Endpoint.Authority}.";
			AppendTranscript(
				"tool",
				BuildToolCallTitle(approvalRequest.Tool),
				BuildToolCallSummary(approvalRequest.Tool, approvalRequest.ArgumentsJson),
				approvalRequest.ArgumentsJson);

			try
			{
				var result = await toolInvoker.InvokeAsync(approvalRequest.Endpoint, approvalRequest.Tool, approvalRequest.ArgumentsJson, cancellationToken);
				LastResultText = result.DisplayText;
				RestoreReadyState();
				AppendTranscript(
					"result",
					BuildToolResultTitle(approvalRequest.Tool),
					BuildToolResultSummary(approvalRequest.Tool, approvalRequest.ArgumentsJson, result.DisplayText),
					BuildToolResultPayload(result));
				logService.Log("tool-result", $"{approvalRequest.Tool.Name}: {result.DisplayText}");
				NotifyStateChanged();
				return result;
			}
			catch (HttpRequestException ex)
			{
				HandleToolInvocationFailure(approvalRequest, ex);
			}
			catch (IOException ex)
			{
				HandleToolInvocationFailure(approvalRequest, ex);
			}
			catch (InvalidOperationException ex)
			{
				HandleToolInvocationFailure(approvalRequest, ex);
			}
			catch (JsonException ex)
			{
				HandleToolInvocationFailure(approvalRequest, ex);
			}
			catch (NotSupportedException ex)
			{
				HandleToolInvocationFailure(approvalRequest, ex);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}

			NotifyStateChanged();
			return null;
		}

		async Task CompleteToolBackedTurnAsync(string originalPrompt, IReadOnlyList<AssistantConversationTurn> conversationHistory, IReadOnlyList<AssistantToolExecution> toolExecutionHistory, CancellationToken cancellationToken)
		{
			try
			{
				var currentToolExecutionHistory = toolExecutionHistory.ToList();
				while (currentToolExecutionHistory.Count > 0)
				{
					ConnectionState = McpConnectionState.GeneratingResponse;
					StatusText = $"Generating with {SelectedModel!.DisplayName}";
					ActivityText = currentToolExecutionHistory.Count == 1
						? "Sending the tool result back to the model for a final response."
						: "Sending the latest tool result back to the model for the next step.";
					NotifyStateChanged();
					var streamedResponse = new StreamedAssistantResponseState();

					var latestToolExecution = currentToolExecutionHistory[^1];
					var followUpResponse = await assistantChatService.GenerateResponseAsync(
						new AssistantChatRequest
						{
							Prompt = originalPrompt,
							Model = SelectedModel!,
							Endpoint = ResolvedEndpoint,
							AvailableTools = AvailableTools,
							ConversationHistory = conversationHistory,
							ToolExecutionHistory = currentToolExecutionHistory,
							ToolCallIntent = latestToolExecution.ToolCallIntent,
							ToolResultText = latestToolExecution.ToolResultText,
							ToolResultRawJson = latestToolExecution.ToolResultRawJson,
						},
						CreateAssistantStreamHandler(streamedResponse),
						cancellationToken);

					FinalizeAssistantResponse(streamedResponse, followUpResponse.Message);

					if (followUpResponse.ToolCallIntent is null || AssistantMode == McpAssistantMode.Chat)
					{
						RestoreReadyState();
						NotifyStateChanged();
						return;
					}

					if (currentToolExecutionHistory.Count >= MaxToolCallsPerTurn)
					{
						HandlePromptFailure(new InvalidOperationException($"LM Studio requested more than {MaxToolCallsPerTurn} tool calls in one turn. The sidecar stopped to avoid an unbounded loop."));
						return;
					}

					var nextToolResult = await InvokeToolIntentAsync(followUpResponse.ToolCallIntent, originalPrompt, cancellationToken, currentToolExecutionHistory);
					if (nextToolResult is null)
					{
						return;
					}

					currentToolExecutionHistory.Add(new AssistantToolExecution
					{
						ToolCallIntent = followUpResponse.ToolCallIntent,
						ToolResultText = nextToolResult.DisplayText,
						ToolResultRawJson = nextToolResult.RawJson,
					});
				}
			}
			catch (HttpRequestException ex)
			{
				HandlePromptFailure(ex);
			}
			catch (IOException ex)
			{
				HandlePromptFailure(ex);
			}
			catch (InvalidOperationException ex)
			{
				HandlePromptFailure(ex);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				HandlePromptCanceled();
			}
			catch (JsonException ex)
			{
				HandlePromptFailure(ex);
			}
			catch (NotSupportedException ex)
			{
				HandlePromptFailure(ex);
			}
		}

		static IReadOnlyList<AssistantToolExecution> AppendToolExecution(IReadOnlyList<AssistantToolExecution> toolExecutionHistory, AssistantToolCallIntent toolCallIntent, McpToolCallResult toolResult)
		{
			var result = toolExecutionHistory.ToList();
			result.Add(new AssistantToolExecution
			{
				ToolCallIntent = toolCallIntent,
				ToolResultText = toolResult.DisplayText,
				ToolResultRawJson = toolResult.RawJson,
			});
			return result;
		}

		void RestoreReadyState()
		{
			ActivityText = null;
			ConnectionState = ResolvedEndpoint is null
				? McpConnectionState.EndpointUnresolved
				: AvailableTools.Count > 0
					? McpConnectionState.Connected
					: McpConnectionState.Disconnected;

			StatusText = ConnectionState switch
			{
				McpConnectionState.EndpointUnresolved => "Endpoint unresolved",
				McpConnectionState.Connected => "Connected",
				_ => "Disconnected",
			};
		}

		void HandleConnectFailure(Exception ex)
		{
			ActivityText = null;
			ConnectionState = McpConnectionState.DiscoveryFailed;
			StatusText = "Discovery failed";
			AvailableTools = toolCatalog.GetBuiltInTools();
			SelectedToolName = null;
			AppendTranscript("error", "Connection failed", ex.Message);
			logger.LogWarning(ex, "Failed to connect or discover MCP tools from {endpoint}", ResolvedEndpoint);
			logService.Log("discovery-error", ex.Message);
		}

		void HandlePromptFailure(Exception ex)
		{
			var failureMessage = BuildPromptFailureMessage(ex);
			LastResultText = failureMessage;
			ActivityText = null;
			ConnectionState = McpConnectionState.ModelResponseFailed;
			StatusText = "Model response failed";
			AppendTranscript("error", "Model response failed", failureMessage);
			logService.Log("model-error", ex.Message);
			NotifyStateChanged();
		}

		void HandlePromptCanceled()
		{
			LastResultText = "Canceled the current assistant request.";
			RestoreReadyState();
			AppendTranscript("system", "Request canceled", LastResultText);
			NotifyStateChanged();
		}

		void HandlePromptValidationFailure(string message)
		{
			LastResultText = message;
			AppendTranscript("error", "Prompt incomplete", message);
			logService.Log("model-warn", message);
		}

		string BuildUnavailableToolMessage(string toolName)
		{
			return ConnectionState == McpConnectionState.Connected
				? $"Tool '{toolName}' is not in the loaded tool catalog. Reconnect to refresh tools, or confirm the exact tool name in the Tools tab."
				: $"Tool '{toolName}' is not available yet. Connect first so the MCP tool catalog can load, then retry the request.";
		}

		static string BuildPromptFailureMessage(Exception ex)
		{
			if (ex.Message.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase))
			{
				return "The model could not process the tool result because the follow-up payload exceeded the available context window. Narrow the request, use a smaller inspection mode, or reduce the returned tool data before retrying.";
			}

			if (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
			{
				return ex.Message + " The request likely exceeded the model or HTTP pipeline time budget before LM Studio finished generating a response.";
			}

			return ex.Message;
		}

		CancellationTokenSource BeginPromptOperation()
		{
			if (currentPromptOperationCts is not null)
			{
				throw new InvalidOperationException("Another assistant request is already in progress.");
			}

			currentPromptOperationCts = new CancellationTokenSource();
			return currentPromptOperationCts;
		}

		void EndPromptOperation(CancellationTokenSource promptOperationCts)
		{
			if (!ReferenceEquals(currentPromptOperationCts, promptOperationCts))
			{
				return;
			}

			currentPromptOperationCts = null;
			promptOperationCts.Dispose();
		}

		sealed class PromptSubmissionPreparation
		{
			public static PromptSubmissionPreparation Empty { get; } = new PromptSubmissionPreparation(string.Empty, null, null);

			public PromptSubmissionPreparation(string promptForModel, McpToolDescriptor? mentionedTool, AssistantToolCallIntent? directToolIntent)
			{
				PromptForModel = promptForModel;
				MentionedTool = mentionedTool;
				DirectToolIntent = directToolIntent;
			}

			public string PromptForModel { get; }

			public McpToolDescriptor? MentionedTool { get; }

			public AssistantToolCallIntent? DirectToolIntent { get; }
		}

		sealed class StreamedAssistantResponseState
		{
			public int? EntryIndex { get; set; }

			public StringBuilder Message { get; } = new StringBuilder();
		}

		void HandleToolInvocationFailure(McpApprovalRequest approvalRequest, Exception ex)
		{
			LastResultText = ex.Message;
			ActivityText = null;
			ConnectionState = McpConnectionState.ToolInvocationFailed;
			StatusText = "Tool invocation failed";
			AppendTranscript("error", "Tool invocation failed", ex.Message);
			logService.Log("tool-error", $"{approvalRequest.Tool.Name}: {ex.Message}");
		}

		public void ClearTranscript()
		{
			transcriptStore.Clear();
			transcriptEntries.Clear();
			AppendTranscript("system", "Transcript cleared", "Cleared local MCP transcript history.");
		}

		void AppendTranscript(string kind, string message)
		{
			AppendTranscript(kind, GetDefaultTitle(kind), message, null);
		}

		/// <summary>
		/// Builds prior conversation turns from the transcript for the LLM. Only user and assistant turns
		/// are included; status, tool, approval, and error entries are skipped. Capped at the 20 most
		/// recent qualifying entries (10 user-assistant pairs) to limit token usage.
		/// </summary>
		IReadOnlyList<AssistantConversationTurn> BuildConversationHistory()
		{
			const int MaxHistoryEntries = 20;
			return TranscriptEntries
				.Where(e => string.Equals(e.Kind, "user", StringComparison.Ordinal)
						 || string.Equals(e.Kind, "assistant", StringComparison.Ordinal))
				.TakeLast(MaxHistoryEntries)
				.Select(e => new AssistantConversationTurn { Role = e.Kind, Content = e.Message })
				.ToList();
		}

		void AppendTranscript(string kind, string title, string message, string? payload = null)
		{
			var entry = new McpTranscriptEntry
			{
				Timestamp = DateTimeOffset.UtcNow,
				Kind = kind,
				Title = title,
				Message = message,
				Payload = payload,
			};

			AddTranscriptEntry(entry, persist: true);
		}

		Action<string> CreateAssistantStreamHandler(StreamedAssistantResponseState streamedResponse)
		{
			return delta =>
			{
				if (string.IsNullOrEmpty(delta))
				{
					return;
				}

				streamedResponse.Message.Append(delta);
				var partialMessage = streamedResponse.Message.ToString();
				LastResultText = partialMessage;
				ActivityText = SelectedModel is null
					? "Receiving the assistant response."
					: $"Receiving the response from {SelectedModel.DisplayName}.";

				if (streamedResponse.EntryIndex is null)
				{
					streamedResponse.EntryIndex = AddTranscriptEntry(
						new McpTranscriptEntry
						{
							Timestamp = DateTimeOffset.UtcNow,
							Kind = "assistant",
							Title = GetDefaultTitle("assistant"),
							Message = partialMessage,
						},
						persist: false);
					return;
				}

				UpdateTranscriptEntry(streamedResponse.EntryIndex.Value, entry => new McpTranscriptEntry
				{
					Timestamp = entry.Timestamp,
					Kind = entry.Kind,
					Title = entry.Title,
					Message = partialMessage,
					Payload = entry.Payload,
				});
			};
		}

		void FinalizeAssistantResponse(StreamedAssistantResponseState streamedResponse, string message)
		{
			LastResultText = message;
			if (string.IsNullOrWhiteSpace(message))
			{
				if (streamedResponse.EntryIndex is not null)
				{
					RemoveTranscriptEntry(streamedResponse.EntryIndex.Value);
				}

				return;
			}

			if (streamedResponse.EntryIndex is null)
			{
				AppendTranscript("assistant", message);
				return;
			}

			UpdateTranscriptEntry(
				streamedResponse.EntryIndex.Value,
				entry => new McpTranscriptEntry
				{
					Timestamp = entry.Timestamp,
					Kind = entry.Kind,
					Title = entry.Title,
					Message = message,
					Payload = entry.Payload,
				},
				notify: false);
			PersistTranscriptEntry(transcriptEntries[streamedResponse.EntryIndex.Value]);
			NotifyStateChanged();
		}

		void DiscardEmptyAssistantResponse(StreamedAssistantResponseState streamedResponse)
		{
			if (streamedResponse.EntryIndex is null || streamedResponse.Message.Length > 0)
			{
				return;
			}

			RemoveTranscriptEntry(streamedResponse.EntryIndex.Value);
		}

		int AddTranscriptEntry(McpTranscriptEntry entry, bool persist)
		{
			if (persist)
			{
				PersistTranscriptEntry(entry);
			}

			transcriptEntries.Add(entry);
			TrimTranscriptEntries();
			NotifyStateChanged();
			return transcriptEntries.Count - 1;
		}

		void UpdateTranscriptEntry(int index, Func<McpTranscriptEntry, McpTranscriptEntry> update, bool notify = true)
		{
			if ((uint)index >= (uint)transcriptEntries.Count)
			{
				return;
			}

			transcriptEntries[index] = update(transcriptEntries[index]);
			if (notify)
			{
				NotifyStateChanged();
			}
		}

		void RemoveTranscriptEntry(int index)
		{
			if ((uint)index >= (uint)transcriptEntries.Count)
			{
				return;
			}

			transcriptEntries.RemoveAt(index);
			NotifyStateChanged();
		}

		void PersistTranscriptEntry(McpTranscriptEntry entry)
		{
			transcriptStore.Append(entry);
			logger.LogInformation("MCP transcript {kind}: {message}", entry.Kind, entry.Message);
		}

		void TrimTranscriptEntries()
		{
			var overflow = transcriptEntries.Count - DefaultMaxEntries;
			if (overflow > 0)
			{
				transcriptEntries.RemoveRange(0, overflow);
			}
		}

		static string BuildToolResultTitle(McpToolDescriptor tool)
		{
			return string.IsNullOrWhiteSpace(tool.Title)
				? $"Tool result - {tool.Name}"
				: $"Tool result - {tool.Title}";
		}

		static string BuildToolCallTitle(McpToolDescriptor tool)
		{
			return string.IsNullOrWhiteSpace(tool.Title)
				? $"Tool call - {tool.Name}"
				: $"Tool call - {tool.Title}";
		}

		static string BuildToolCallSummary(McpToolDescriptor tool, string argumentsJson)
		{
			var args = TryParseArguments(argumentsJson);
			return tool.Name switch
			{
				"file_read" => BuildFileReadCallSummary(args),
				"file_write" => BuildFileWriteCallSummary(args),
				"run_command" => BuildRunCommandCallSummary(args),
				"search_web" => BuildSearchWebCallSummary(args),
				_ => $"Invoking {tool.Name}.",
			};
		}

		static string BuildToolResultSummary(McpToolDescriptor tool, string argumentsJson, string displayText)
		{
			var normalizedOutput = McpJsonFormatting.NormalizeText(displayText, out _);
			var lineCount = CountLines(normalizedOutput);
			var args = TryParseArguments(argumentsJson);

			return tool.Name switch
			{
				"file_read" => BuildFileReadSummary(args, lineCount),
				"file_write" => BuildFileWriteSummary(args),
				"run_command" => BuildRunCommandSummary(args, lineCount, normalizedOutput),
				"search_web" => BuildSearchWebSummary(args, lineCount),
				_ => BuildGenericToolSummary(tool, lineCount),
			};
		}

		static string? BuildToolResultPayload(McpToolCallResult result)
		{
			if (!string.IsNullOrWhiteSpace(result.RawJson) && !string.Equals(result.RawJson.Trim(), "{}", StringComparison.Ordinal))
			{
				return result.RawJson;
			}

			return string.IsNullOrWhiteSpace(result.DisplayText) ? null : result.DisplayText;
		}

		static JsonObject? TryParseArguments(string argumentsJson)
		{
			if (string.IsNullOrWhiteSpace(argumentsJson))
			{
				return null;
			}

			try
			{
				return JsonNode.Parse(argumentsJson) as JsonObject;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		static string BuildFileReadSummary(JsonObject? args, int lineCount)
		{
			var path = TryGetString(args, "path");
			var fileName = string.IsNullOrWhiteSpace(path) ? "file" : Path.GetFileName(path);
			return $"Read {fileName} successfully. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")}.";
		}

		static string BuildFileReadCallSummary(JsonObject? args)
		{
			var path = TryGetString(args, "path");
			var fileName = string.IsNullOrWhiteSpace(path) ? "file" : Path.GetFileName(path);
			return $"Reading {fileName}.";
		}

		static string BuildFileWriteSummary(JsonObject? args)
		{
			var path = TryGetString(args, "path");
			var fileName = string.IsNullOrWhiteSpace(path) ? "file" : Path.GetFileName(path);
			return $"Wrote {fileName} successfully. Full result is available in the detail view.";
		}

		static string BuildFileWriteCallSummary(JsonObject? args)
		{
			var path = TryGetString(args, "path");
			var fileName = string.IsNullOrWhiteSpace(path) ? "file" : Path.GetFileName(path);
			return $"Writing {fileName}.";
		}

		static string BuildRunCommandSummary(JsonObject? args, int lineCount, string output)
		{
			var command = TryGetString(args, "command");
			var compactCommand = Compact(command, 48);
			var hasOutput = !string.IsNullOrWhiteSpace(output) && !string.Equals(output.Trim(), "(no output)", StringComparison.Ordinal);
			if (!hasOutput)
			{
				return string.IsNullOrWhiteSpace(compactCommand)
					? "Command completed with no output."
					: $"Command completed with no output: {compactCommand}.";
			}

			return string.IsNullOrWhiteSpace(compactCommand)
				? $"Command completed. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")} of output."
				: $"Command completed: {compactCommand}. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")} of output.";
		}

		static string BuildRunCommandCallSummary(JsonObject? args)
		{
			var command = Compact(TryGetString(args, "command"), 48);
			return string.IsNullOrWhiteSpace(command)
				? "Running command."
				: $"Running command: {command}.";
		}

		static string BuildSearchWebSummary(JsonObject? args, int lineCount)
		{
			var url = TryGetString(args, "url");
			return string.IsNullOrWhiteSpace(url)
				? $"Fetched web content successfully. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")}."
				: $"Fetched {Compact(url, 64)} successfully. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")}.";
		}

		static string BuildSearchWebCallSummary(JsonObject? args)
		{
			var url = Compact(TryGetString(args, "url"), 64);
			return string.IsNullOrWhiteSpace(url)
				? "Fetching web content."
				: $"Fetching {url}.";
		}

		static string BuildGenericToolSummary(McpToolDescriptor tool, int lineCount)
		{
			return $"{tool.Name} completed successfully. Returned {lineCount} {(lineCount == 1 ? "line" : "lines")} of data.";
		}

		static string? TryGetString(JsonObject? args, string propertyName)
		{
			var value = args?[propertyName]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			return value;
		}

		static int CountLines(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return 0;
			}

			var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
			return normalized.Split('\n').Length;
		}

		static string Compact(string? text, int maxLength)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}

			var trimmed = text.Trim();
			return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
		}

		void Persist()
		{
			configurationStore.Save(new McpSidecarConfiguration
			{
				IsVisible = IsVisible,
				AutoConnectOnStartup = AutoConnectOnStartup,
				PanelWidth = PanelWidth,
				EndpointOverride = EndpointOverride,
				SelectedModelId = SelectedModel?.Id,
			});
		}

		static string GetDefaultTitle(string kind)
		{
			return kind switch
			{
				"user" => "You",
				"assistant" => "Assistant",
				"working" => "Working",
				"considering" => "Considering",
				"processing" => "Processing",
				"tool" => "Tool call",
				"result" => "Tool result",
				"approval" => "Approval",
				"config" => "Configuration",
				"model" => "Model",
				"error" => "Error",
				_ => "System",
			};
		}

		static bool UriEquals(Uri? left, Uri? right)
		{
			if (left is null && right is null)
			{
				return true;
			}

			if (left is null || right is null)
			{
				return false;
			}

			return Uri.Compare(left, right, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
		}

		void NotifyStateChanged()
		{
			StateChanged?.Invoke(this, EventArgs.Empty);
		}

		void OnHubActionReceived(object? sender, McpHubActionEventArgs ev)
		{
			void Append()
			{
				var status = ev.Success ? "succeeded" : "failed";
				AppendTranscript(
					"hub",
					ev.Success ? "Action completed" : "Action failed",
					$"Tool '{ev.Tool}' \u2192 '{ev.Key}' {status}.");
				NotifyStateChanged();
			}

			if (synchronizationContext is not null)
				synchronizationContext.Post(_ => Append(), null);
			else
				Append();
		}

		async Task StartHubAsync(Uri mcpEndpoint)
		{
			try
			{
				var hubUri = new UriBuilder(mcpEndpoint) { Path = "/copilot-hub", Query = string.Empty }.Uri;
				await hubConnection.StartAsync(hubUri);
				logService.Log("hub", $"SignalR hub connected to '{hubUri}'.");
			}
			catch (Exception ex)
			{
				logService.Log("hub", $"SignalR hub connection failed: {ex.Message}");
			}
		}
	}
}
