using System;
using System.Drawing;
using System.Windows.Forms;

namespace SaasCopilot.Copilot.GUI.Composition
{
	public interface IMcpSidecarPresenter : IDisposable
	{
		bool IsVisible { get; }

		Uri? CurrentEndpoint { get; }

		void OnHostShown();

		void OnApplicationNavigated();

		void Toggle(bool? visible, string? endpointOverride, IWin32Window owner, Rectangle ownerBounds, Rectangle workArea, int ownerHeight);
	}
}
