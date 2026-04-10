using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IAssistantModelSelectionService
	{
		IReadOnlyList<AssistantModelDescriptor> GetAvailableModels();

		Task<IReadOnlyList<AssistantModelDescriptor>> RefreshAvailableModelsAsync(CancellationToken cancellationToken = default);

		AssistantModelDescriptor? GetSelectedModel();

		AssistantModelDescriptor? SetSelectedModel(string? modelId);
	}
}
