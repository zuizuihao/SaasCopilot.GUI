using System;
using System.Drawing;
using System.Windows.Forms;
using SaasCopilot.Copilot.GUI.Features.Mcp;

namespace SaasCopilot.Copilot.GUI.Composition
{
	public sealed class McpSidecarPresenter : IMcpSidecarPresenter
	{
		readonly McpSidecarController controller;
		readonly string title;
		McpSidecarForm? sidecarForm;

		public McpSidecarPresenter(McpSidecarController controller, string title)
		{
			ArgumentNullException.ThrowIfNull(controller);
			ArgumentException.ThrowIfNullOrWhiteSpace(title);

			this.controller = controller;
			this.title = title;
		}

		public bool IsVisible => sidecarForm is not null && sidecarForm.Visible;

		public Uri? CurrentEndpoint => controller.ResolvedEndpoint;

		public void OnHostShown()
		{
			controller.RefreshEndpoint();
			_ = controller.TryAutoConnectAsync();
		}

		public void OnApplicationNavigated()
		{
			controller.RefreshEndpoint();
			_ = controller.TryAutoConnectAsync();
		}

		public void Toggle(bool? visible, string? endpointOverride, IWin32Window owner, Rectangle ownerBounds, Rectangle workArea, int ownerHeight)
		{
			ArgumentNullException.ThrowIfNull(owner);

			if (endpointOverride is not null)
			{
				controller.SetEndpointOverride(endpointOverride);
			}

			SetVisibility(visible ?? !controller.IsVisible, owner, ownerBounds, workArea, ownerHeight);
			controller.RefreshEndpoint();
		}

		public void Dispose()
		{
			sidecarForm?.Dispose();
			sidecarForm = null;
		}

		void SetVisibility(bool visible, IWin32Window owner, Rectangle ownerBounds, Rectangle workArea, int ownerHeight)
		{
			if (visible && sidecarForm is null)
			{
				InitializeSidecar(ownerBounds, workArea, ownerHeight);
			}

			controller.SetVisible(visible);
			ApplyLayout(owner);
		}

		void InitializeSidecar(Rectangle ownerBounds, Rectangle workArea, int ownerHeight)
		{
			var formWidth = Math.Max(480, controller.PanelWidth);
			var formHeight = Math.Max(500, ownerHeight);
			sidecarForm = new McpSidecarForm(controller, CloseFromSidecar, title)
			{
				Width = formWidth,
				Height = formHeight,
			};
			sidecarForm.ResizeEnd += (_, _) => PersistWidth();

			var x = ownerBounds.Right + 8;
			var y = ownerBounds.Top;
			if (x + formWidth > workArea.Right)
			{
				x = Math.Max(workArea.Left, ownerBounds.Left + 40);
				y = Math.Max(workArea.Top, ownerBounds.Top + 40);
			}

			sidecarForm.Location = new Point(
				Math.Max(workArea.Left, Math.Min(x, workArea.Right - formWidth)),
				Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - formHeight)));
		}

		void ApplyLayout(IWin32Window? owner)
		{
			if (sidecarForm is null)
			{
				return;
			}

			if (controller.IsVisible)
			{
				if (!sidecarForm.Visible)
				{
					if (owner is null)
					{
						sidecarForm.Show();
					}
					else
					{
						sidecarForm.Show(owner);
					}
				}
			}
			else if (sidecarForm.Visible)
			{
				sidecarForm.Hide();
			}
		}

		void PersistWidth()
		{
			if (sidecarForm is null || !sidecarForm.Visible)
			{
				return;
			}

			controller.SetPanelWidth(sidecarForm.Width);
		}

		void CloseFromSidecar()
		{
			controller.SetVisible(false);
			ApplyLayout(owner: null);
		}
	}
}
