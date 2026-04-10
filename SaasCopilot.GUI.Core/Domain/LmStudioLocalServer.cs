using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public static class LmStudioLocalServer
	{
		public static readonly Uri BaseUri = new Uri("http://localhost:1234/v1/", UriKind.Absolute);

		public static readonly Uri ModelsUri = new Uri(BaseUri, "models");

		public static readonly Uri ChatCompletionsUri = new Uri(BaseUri, "chat/completions");

		// LM Studio's own REST API — not OpenAI-compatible, but returns richer model metadata such as context_length.
		public static readonly Uri V0ModelsUri = new Uri("http://localhost:1234/api/v0/models", UriKind.Absolute);

		public const string ApiKey = "lm-studio";
	}
}
