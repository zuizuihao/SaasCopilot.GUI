using System;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public interface IMcpTrustStore
	{
		bool IsTrusted(Uri endpoint, string toolName);

		void SaveTrust(Uri endpoint, string toolName);
	}
}
