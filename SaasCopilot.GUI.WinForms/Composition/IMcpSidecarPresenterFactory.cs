using System;

namespace SaasCopilot.Copilot.GUI.Composition
{
	public interface IMcpSidecarPresenterFactory
	{
		IMcpSidecarPresenter Create(Func<Uri?> activeApplicationUriProvider, string title);
	}
}
