using System;
using System.Drawing;
using System.Windows.Forms;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpSidecarSettingsForm : Form
	{
		static readonly Padding SectionMargin = new Padding(0, 0, 0, 14);

		readonly TextBox endpointOverrideTextBox;
		readonly CheckBox autoConnectCheckBox;

		public McpSidecarSettingsForm(string? endpointOverride, bool autoConnectOnStartup, Uri? resolvedEndpoint, string? resolutionFailureReason)
		{
			Text = "MCP Configuration";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(840, 800);
			MinimumSize = new Size(840, 800);
			BackColor = Color.FromArgb(244, 239, 231);
			Font = new Font("Segoe UI", 9F, FontStyle.Regular);

			var rootLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				Padding = new Padding(20),
				BackColor = BackColor,
			};
			rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var contentPanel = new Panel
			{
				Dock = DockStyle.Fill,
				AutoScroll = true,
				BackColor = BackColor,
				Margin = new Padding(0),
			};

			var contentLayout = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				BackColor = BackColor,
				Margin = new Padding(0),
				Padding = new Padding(0),
			};

			var introPanel = new Panel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(16),
				Margin = SectionMargin,
				Width = 560,
			};

			var introLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				AutoSize = true,
				BackColor = Color.White,
			};

			var titleLabel = new Label
			{
				AutoSize = true,
				Text = "Assistant connection settings",
				Font = new Font("Segoe UI", 13F, FontStyle.Bold),
				ForeColor = Color.FromArgb(50, 43, 37),
				Margin = new Padding(0),
			};

			var introLabel = new Label
			{
				AutoSize = true,
				Text = "Choose whether the assistant should resolve MCP from the active application URL or use an override, and decide if it should connect automatically on startup.",
				MaximumSize = new Size(560, 0),
				ForeColor = Color.FromArgb(92, 82, 71),
				Margin = new Padding(0, 8, 0, 0),
			};
			introLayout.Controls.Add(titleLabel, 0, 0);
			introLayout.Controls.Add(introLabel, 0, 1);
			introPanel.Controls.Add(introLayout);

			var endpointPanel = new Panel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(16),
				Margin = SectionMargin,
				Width = 560,
			};

			var endpointLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 6,
				AutoSize = true,
				BackColor = Color.White,
			};

			var endpointTitleLabel = new Label
			{
				AutoSize = true,
				Text = "MCP endpoint",
				Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(50, 43, 37),
				Margin = new Padding(0),
			};
			endpointLayout.Controls.Add(endpointTitleLabel, 0, 0);

			var endpointHelpLabel = new Label
			{
				AutoSize = true,
				Text = "Leave the override blank to resolve MCP from the active application page. Use an override when you need to force a specific local or remote endpoint.",
				MaximumSize = new Size(560, 0),
				ForeColor = Color.FromArgb(92, 82, 71),
				Margin = new Padding(0, 8, 0, 14),
			};
			endpointLayout.Controls.Add(endpointHelpLabel, 0, 1);

			var endpointOverrideLabel = new Label
			{
				AutoSize = true,
				Text = "Endpoint override",
				Font = new Font("Segoe UI", 9F, FontStyle.Bold),
				Margin = new Padding(0, 0, 0, 6),
			};
			endpointLayout.Controls.Add(endpointOverrideLabel, 0, 2);

			endpointOverrideTextBox = new TextBox
			{
				Dock = DockStyle.Top,
				Text = endpointOverride ?? string.Empty,
				PlaceholderText = "Leave blank to resolve from the active application URL.",
				Margin = new Padding(0, 0, 0, 14),
				MinimumSize = new Size(0, 30),
			};

			endpointLayout.Controls.Add(endpointOverrideTextBox, 0, 3);

			var resolvedEndpointCaptionLabel = new Label
			{
				AutoSize = true,
				Text = "Resolved endpoint",
				Font = new Font("Segoe UI", 9F, FontStyle.Bold),
				Margin = new Padding(0, 0, 0, 6),
			};
			endpointLayout.Controls.Add(resolvedEndpointCaptionLabel, 0, 4);

			var resolvedEndpointLabel = new Label
			{
				AutoSize = true,
				Text = resolvedEndpoint is null
					? resolutionFailureReason ?? "No endpoint is currently resolved."
					: $"Currently resolved endpoint: {resolvedEndpoint}",
				ForeColor = Color.FromArgb(73, 65, 57),
				MaximumSize = new Size(560, 0),
				BackColor = Color.FromArgb(249, 246, 240),
				Padding = new Padding(12, 10, 12, 10),
				BorderStyle = BorderStyle.FixedSingle,
			};
			endpointLayout.Controls.Add(resolvedEndpointLabel, 0, 5);

			endpointPanel.Controls.Add(endpointLayout);

			var startupPanel = new Panel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(16),
				Margin = new Padding(0),
				Width = 560,
			};

			var startupLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				AutoSize = true,
				BackColor = Color.White,
			};

			var startupTitleLabel = new Label
			{
				AutoSize = true,
				Text = "Startup behavior",
				Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(50, 43, 37),
				Margin = new Padding(0),
			};

			var startupHelpLabel = new Label
			{
				AutoSize = true,
				Text = "When enabled, the assistant attempts to connect as soon as a resolvable MCP endpoint is available. Disable it if you want to connect manually.",
				MaximumSize = new Size(560, 0),
				ForeColor = Color.FromArgb(92, 82, 71),
				Margin = new Padding(0, 8, 0, 14),
			};

			autoConnectCheckBox = new CheckBox
			{
				AutoSize = true,
				Text = "Connect to MCP automatically when the assistant starts or the app endpoint changes",
				Checked = autoConnectOnStartup,
				Margin = new Padding(0),
			};

			startupLayout.Controls.Add(startupTitleLabel, 0, 0);
			startupLayout.Controls.Add(startupHelpLabel, 0, 1);
			startupLayout.Controls.Add(autoConnectCheckBox, 0, 2);
			startupPanel.Controls.Add(startupLayout);

			contentLayout.Controls.Add(introPanel);
			contentLayout.Controls.Add(endpointPanel);
			contentLayout.Controls.Add(startupPanel);
			contentPanel.Controls.Add(contentLayout);

			var buttonsPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				AutoSize = true,
				Margin = new Padding(0, 14, 0, 0),
				BackColor = BackColor,
			};

			var saveButton = new Button
			{
				AutoSize = true,
				Text = "Save",
				DialogResult = DialogResult.OK,
				MinimumSize = new Size(92, 36),
				BackColor = Color.FromArgb(43, 93, 71),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(8, 0, 0, 0),
			};
			saveButton.FlatAppearance.BorderSize = 0;

			var cancelButton = new Button
			{
				AutoSize = true,
				Text = "Cancel",
				DialogResult = DialogResult.Cancel,
				MinimumSize = new Size(92, 36),
				BackColor = Color.White,
				FlatStyle = FlatStyle.Flat,
			};
			cancelButton.FlatAppearance.BorderColor = Color.FromArgb(195, 180, 158);

			buttonsPanel.Controls.Add(saveButton);
			buttonsPanel.Controls.Add(cancelButton);

			AcceptButton = saveButton;
			CancelButton = cancelButton;

			rootLayout.Controls.Add(contentPanel, 0, 0);
			rootLayout.Controls.Add(buttonsPanel, 0, 1);

			Controls.Add(rootLayout);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				endpointOverrideTextBox.Dispose();
				autoConnectCheckBox.Dispose();
			}

			base.Dispose(disposing);
		}

		public string? EndpointOverride => string.IsNullOrWhiteSpace(endpointOverrideTextBox.Text) ? null : endpointOverrideTextBox.Text.Trim();

		public bool AutoConnectOnStartup => autoConnectCheckBox.Checked;
	}
}
