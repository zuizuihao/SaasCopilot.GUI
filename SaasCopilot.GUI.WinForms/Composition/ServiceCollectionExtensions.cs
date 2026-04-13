using System;
using System.Threading;
using SaasCopilot.Copilot.GUI.Features.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace SaasCopilot.Copilot.GUI.Composition
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddSaasCopilotCopilotGui(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.AddHttpClient(AssistantChatService.HttpClientName, httpClient => httpClient.Timeout = Timeout.InfiniteTimeSpan);
			services.AddSingleton<IMcpEndpointResolver, McpEndpointResolver>();
			services.AddSingleton<IMcpConfigurationStore, McpConfigurationStore>();
			services.AddSingleton<IAssistantChatService, AssistantChatService>();
			services.AddSingleton<IAssistantModelSelectionService, AssistantModelSelectionService>();
			services.AddSingleton<IMcpTranscriptStore, McpTranscriptStore>();
			services.AddSingleton<IMcpLogService, McpLogService>();
			services.AddSingleton<IMcpTrustStore, McpTrustStore>();
			services.AddSingleton<IMcpApprovalPolicy, McpApprovalPolicy>();
			services.AddSingleton<IMcpApprovalService, McpApprovalService>();
			services.AddSingleton<IMcpConnectionManager, McpConnectionManager>();
			services.AddSingleton<IMcpHubConnection, McpHubConnection>();
			services.AddSingleton<IBuiltInToolProvider, BuiltInToolProvider>();
			services.AddSingleton<IBuiltInToolExecutor, BuiltInToolExecutor>();
			services.AddSingleton<IMcpToolCatalog, McpToolCatalog>();
			services.AddSingleton<IMcpToolInvoker, McpToolInvoker>();
			services.AddSingleton<IMcpSidecarPresenterFactory, McpSidecarPresenterFactory>();
			return services;
		}
	}
}
