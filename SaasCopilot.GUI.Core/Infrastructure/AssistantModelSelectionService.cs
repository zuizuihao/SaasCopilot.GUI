using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantModelSelectionService : IAssistantModelSelectionService
	{
		readonly IHttpClientFactory httpClientFactory;

		readonly IMcpConfigurationStore configurationStore;
		readonly IMcpLogService logService;
		readonly ILogger<AssistantModelSelectionService> logger;

		IReadOnlyList<AssistantModelDescriptor>? cachedModels;

		public AssistantModelSelectionService(
			IMcpConfigurationStore configurationStore,
			IHttpClientFactory httpClientFactory,
			IMcpLogService logService,
			ILogger<AssistantModelSelectionService> logger)
		{
			ArgumentNullException.ThrowIfNull(configurationStore);
			ArgumentNullException.ThrowIfNull(httpClientFactory);
			ArgumentNullException.ThrowIfNull(logService);
			ArgumentNullException.ThrowIfNull(logger);

			this.configurationStore = configurationStore;
			this.httpClientFactory = httpClientFactory;
			this.logService = logService;
			this.logger = logger;
		}

		public IReadOnlyList<AssistantModelDescriptor> GetAvailableModels()
		{
			cachedModels ??= CreateFallbackModels();
			return cachedModels;
		}

		public async Task<IReadOnlyList<AssistantModelDescriptor>> RefreshAvailableModelsAsync(CancellationToken cancellationToken = default)
		{
			cachedModels = await LoadModelsAsync(cancellationToken).ConfigureAwait(false);
			return cachedModels;
		}

		public AssistantModelDescriptor? GetSelectedModel()
		{
			var configuration = configurationStore.Load();
			var selectedModel = ResolveModel(configuration.SelectedModelId, GetAvailableModels());

			if (selectedModel is not null && !string.Equals(configuration.SelectedModelId, selectedModel.Id, StringComparison.Ordinal))
			{
				SaveSelection(configuration, selectedModel.Id);
			}

			return selectedModel;
		}

		public AssistantModelDescriptor? SetSelectedModel(string? modelId)
		{
			var configuration = configurationStore.Load();
			var selectedModel = ResolveModel(modelId, GetAvailableModels());
			if (selectedModel is not null)
			{
				SaveSelection(configuration, selectedModel.Id);
			}
			return selectedModel;
		}

		async Task<AssistantModelDescriptor[]> LoadModelsAsync(CancellationToken cancellationToken)
		{
			try
			{
				using var httpClient = httpClientFactory.CreateClient();

				// Prefer the LM Studio v0 API — it returns richer context-window metadata than the
				// OpenAI-compatible endpoint, including loaded_context_length and max_context_length.
				// Fall back to the OpenAI-compatible v1 endpoint when v0 is unavailable.
				var (modelsNode, sourceUri) = await TryLoadV0ModelsAsync(httpClient, cancellationToken).ConfigureAwait(false)
					?? await LoadV1ModelsAsync(httpClient, cancellationToken).ConfigureAwait(false);

				if (modelsNode is null || modelsNode.Count == 0)
				{
					throw new InvalidOperationException("LM Studio returned no models.");
				}

				var models = new List<AssistantModelDescriptor>();
				foreach (var item in modelsNode)
				{
					var modelId = item?["id"]?.GetValue<string>();
					if (string.IsNullOrWhiteSpace(modelId))
					{
						continue;
					}

					var owner = item?["owned_by"]?.GetValue<string>();
					var contextLength = ResolveContextWindow(item);
					models.Add(new AssistantModelDescriptor
					{
						Id = modelId,
						DisplayName = modelId,
						Description = string.IsNullOrWhiteSpace(owner) ? "LM Studio local model." : $"LM Studio local model owned by '{owner}'.",
						ContextWindow = contextLength,
					});
				}

				if (models.Count == 0)
				{
					throw new InvalidOperationException("LM Studio returned no valid model identifiers.");
				}

				logService.Log("model-discovery", $"Discovered {models.Count} LM Studio model(s) from '{sourceUri}'.");
				return models.ToArray();
			}
			catch (HttpRequestException ex)
			{
				logger.LogWarning(ex, "Falling back to default LM Studio model selection.");
				logService.Log("model-discovery-error", ex.Message);
				return CreateFallbackModels();
			}
			catch (InvalidOperationException ex)
			{
				logger.LogWarning(ex, "Falling back to default LM Studio model selection.");
				logService.Log("model-discovery-error", ex.Message);
				return CreateFallbackModels();
			}
			catch (JsonException ex)
			{
				logger.LogWarning(ex, "Falling back to default LM Studio model selection.");
				logService.Log("model-discovery-error", ex.Message);
				return CreateFallbackModels();
			}
			catch (NotSupportedException ex)
			{
				logger.LogWarning(ex, "Falling back to default LM Studio model selection.");
				logService.Log("model-discovery-error", ex.Message);
				return CreateFallbackModels();
			}
		}

		static int? ResolveContextWindow(JsonNode? modelNode)
		{
			if (TryGetInt32(modelNode?["loaded_context_length"], out var loadedContextLength))
			{
				return loadedContextLength;
			}

			if (TryGetInt32(modelNode?["max_context_length"], out var maxContextLength))
			{
				return maxContextLength;
			}

			if (TryGetInt32(modelNode?["context_length"], out var contextLength))
			{
				return contextLength;
			}

			return null;
		}

		static bool TryGetInt32(JsonNode? valueNode, out int value)
		{
			try
			{
				var parsedValue = valueNode?.GetValue<int?>();
				if (parsedValue is int actualValue)
				{
					value = actualValue;
					return true;
				}
			}
			catch (InvalidOperationException)
			{
			}

			value = default;
			return false;
		}

		async Task<(JsonArray ModelsNode, Uri SourceUri)?> TryLoadV0ModelsAsync(HttpClient httpClient, CancellationToken cancellationToken)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, LmStudioLocalServer.V0ModelsUri);
				using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					return null;
				}

				await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
				var rootNode = await JsonNode.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);
				var modelsNode = rootNode?["data"]?.AsArray();
				if (modelsNode is null || modelsNode.Count == 0)
				{
					return null;
				}

				return (modelsNode, LmStudioLocalServer.V0ModelsUri);
			}
			catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
			{
				logger.LogDebug(ex, "LM Studio v0 models endpoint unavailable; will fall back to v1.");
				return null;
			}
		}

		static async Task<(JsonArray ModelsNode, Uri SourceUri)> LoadV1ModelsAsync(HttpClient httpClient, CancellationToken cancellationToken)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, LmStudioLocalServer.ModelsUri);
			request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {LmStudioLocalServer.ApiKey}");
			using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"LM Studio model discovery failed with status {(int)response.StatusCode}.");
			}

			await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			var rootNode = await JsonNode.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);
			var modelsNode = rootNode?["data"]?.AsArray()
				?? throw new InvalidOperationException("LM Studio returned no models.");
			return (modelsNode, LmStudioLocalServer.ModelsUri);
		}

		static AssistantModelDescriptor[] CreateFallbackModels()
		{
			return Array.Empty<AssistantModelDescriptor>();
		}

		static AssistantModelDescriptor? ResolveModel(string? modelId, IReadOnlyList<AssistantModelDescriptor> availableModels)
		{
			foreach (var model in availableModels)
			{
				if (string.Equals(model.Id, modelId, StringComparison.Ordinal))
				{
					return model;
				}
			}

			return availableModels.Count > 0 ? availableModels[0] : null;
		}

		void SaveSelection(McpSidecarConfiguration configuration, string selectedModelId)
		{
			configurationStore.Save(new McpSidecarConfiguration
			{
				IsVisible = configuration.IsVisible,
				AutoConnectOnStartup = configuration.AutoConnectOnStartup,
				PanelWidth = configuration.PanelWidth,
				EndpointOverride = configuration.EndpointOverride,
				SelectedModelId = selectedModelId,
			});
		}
	}
}
