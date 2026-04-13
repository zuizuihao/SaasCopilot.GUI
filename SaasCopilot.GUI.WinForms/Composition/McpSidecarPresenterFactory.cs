using System;
using SaasCopilot.Copilot.GUI.Features.Mcp;
using Microsoft.Extensions.Logging;

namespace SaasCopilot.Copilot.GUI.Composition
{
	public sealed class McpSidecarPresenterFactory : IMcpSidecarPresenterFactory
	{
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
		readonly IMcpHubConnection hubConnection;

		public McpSidecarPresenterFactory(
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
			this.hubConnection = hubConnection;
		}

		public IMcpSidecarPresenter Create(Func<Uri?> activeApplicationUriProvider, string title)
		{
			ArgumentNullException.ThrowIfNull(activeApplicationUriProvider);
			ArgumentException.ThrowIfNullOrWhiteSpace(title);

			var controller = new McpSidecarController(
				endpointResolver,
				configurationStore,
				modelSelectionService,
				assistantChatService,
				transcriptStore,
				toolCatalog,
				toolInvoker,
				approvalService,
				logService,
				logger,
				activeApplicationUriProvider,
				hubConnection);

			return new McpSidecarPresenter(controller, title);
		}
	}
}
