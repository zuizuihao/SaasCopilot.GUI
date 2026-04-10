using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IAssistantChatService
	{
		Task<AssistantChatResponse> GenerateResponseAsync(AssistantChatRequest request, Action<string>? onMessageDelta = null, CancellationToken cancellationToken = default);
	}
}
