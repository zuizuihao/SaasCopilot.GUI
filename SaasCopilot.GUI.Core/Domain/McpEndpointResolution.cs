using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpEndpointResolution
	{
		public static McpEndpointResolution Success(Uri endpoint) => new McpEndpointResolution(true, endpoint, null);

		public static McpEndpointResolution Failure(string reason) => new McpEndpointResolution(false, null, reason);

		McpEndpointResolution(bool succeeded, Uri? endpoint, string? failureReason)
		{
			Succeeded = succeeded;
			Endpoint = endpoint;
			FailureReason = failureReason;
		}

		public bool Succeeded { get; }

		public Uri? Endpoint { get; }

		public string? FailureReason { get; }
	}
}
