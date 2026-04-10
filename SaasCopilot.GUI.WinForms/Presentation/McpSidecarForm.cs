using System;
using System.Drawing;
using System.Windows.Forms;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpSidecarForm : Form
	{
		readonly Action onClose;

		public McpSidecarForm(McpSidecarController controller, Action onClose, string title)
		{
			ArgumentNullException.ThrowIfNull(controller);
			ArgumentNullException.ThrowIfNull(onClose);
			ArgumentException.ThrowIfNullOrWhiteSpace(title);

			this.onClose = onClose;

			Text = title;
			ShowInTaskbar = false;
			ClientSize = new Size(980, 760);
			MinimumSize = new Size(480, 400);
			FormBorderStyle = FormBorderStyle.SizableToolWindow;
			StartPosition = FormStartPosition.CenterScreen;

			var sidecarControl = new McpSidecarControl(controller, onClose, title)
			{
				Dock = DockStyle.Fill,
			};
			Controls.Add(sidecarControl);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				onClose();
				return;
			}

			base.OnFormClosing(e);
		}
	}
}

