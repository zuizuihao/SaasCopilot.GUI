using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpEndpointResolver : IMcpEndpointResolver
	{
		const string McpPath = "/mcp";

		public McpEndpointResolution Resolve(Uri? activeApplicationUri, string? endpointOverride)
		{
			if (!string.IsNullOrWhiteSpace(endpointOverride))
			{
				if (Uri.TryCreate(endpointOverride, UriKind.Absolute, out var overrideUri)
					&& (overrideUri.Scheme == Uri.UriSchemeHttps || overrideUri.Scheme == Uri.UriSchemeHttp))
				{
					return McpEndpointResolution.Success(overrideUri);
				}

				return McpEndpointResolution.Failure("The endpoint override must be an absolute HTTP or HTTPS URI.");
			}

			if (activeApplicationUri is null)
			{
				return McpEndpointResolution.Failure("No active application URI is available yet.");
			}

			if (activeApplicationUri.Scheme != Uri.UriSchemeHttp && activeApplicationUri.Scheme != Uri.UriSchemeHttps)
			{
				return McpEndpointResolution.Failure($"The current application URI uses the unsupported scheme '{activeApplicationUri.Scheme}'.");
			}

			var builder = new UriBuilder(activeApplicationUri)
			{
				Path = McpPath,
				Query = string.Empty,
				Fragment = string.Empty,
			};

			return McpEndpointResolution.Success(builder.Uri);
		}
	}
}
