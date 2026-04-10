using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpSidecarControl : UserControl
	{
		readonly McpSidecarController controller;
		readonly Action closeSidecar;
		const int PreferredContentPanelMinSize = 220;
		const int NarrowLayoutThreshold = 560;

		readonly Label titleLabel;
		readonly ComboBox modelSelectorComboBox;
		readonly Button connectButton;
		readonly Button moreActionsButton;
		readonly ContextMenuStrip headerActionsMenu;
		readonly ToolStripMenuItem settingsMenuItem;
		readonly ToolStripMenuItem clearTimelineMenuItem;
		readonly ToolStripMenuItem closeMenuItem;
		readonly Label endpointSummaryLabel;
		readonly Label connectionHintLabel;
		readonly Label statusLabel;
		readonly SplitContainer contentSplitContainer;
		readonly Panel timelineHostPanel;
		readonly McpChatTranscriptControl transcriptControl;
		readonly ComboBox toolSelectorComboBox;
		readonly TextBox toolArgumentsTextBox;
		readonly Label toolStateLabel;
		readonly Label toolDescriptionLabel;
		readonly Button invokeToolButton;
		readonly TextBox composerTextBox;
		readonly Label composerStateLabel;
		readonly Label composerBusyIndicatorLabel;
		readonly Timer composerBusyTimer;
		readonly Button sendButton;
		readonly Button modeToggleButton;
		readonly ContextMenuStrip toolSuggestMenu;
		readonly TableLayoutPanel titleRowLayout;
		readonly TableLayoutPanel titleBarLayout;
		readonly FlowLayoutPanel headerActionPanel;
		readonly Label workspaceHintLabel;
		readonly TableLayoutPanel toolHeaderLayout;
		readonly TableLayoutPanel toolActionBar;
		readonly Label toolActionHintLabel;
		readonly TableLayoutPanel composerFooterLayout;
		readonly Label composerModelLabel;
		readonly ToolTip actionToolTip;

		bool isRefreshingView;
		bool isRunningAction;
		bool hasInitializedContentSplit;
		bool canSubmitPrompt;
		int composerBusyFrameIndex;

		static readonly string[] ComposerBusyFrames = { "|", "/", "-", "\\" };

		public McpSidecarControl(McpSidecarController controller, Action closeSidecar, string sidecarTitle)
		{
			ArgumentNullException.ThrowIfNull(controller);
			ArgumentNullException.ThrowIfNull(closeSidecar);
			ArgumentException.ThrowIfNullOrWhiteSpace(sidecarTitle);

			this.controller = controller;
			this.closeSidecar = closeSidecar;

			var backgroundColor = Color.FromArgb(247, 244, 239);
			Dock = DockStyle.Fill;
			BackColor = backgroundColor;
			Padding = new Padding(12);

			var ui = CreateUi(backgroundColor, sidecarTitle);
			titleLabel = ui.TitleLabel;
			modelSelectorComboBox = ui.ModelSelectorComboBox;
			connectButton = ui.ConnectButton;
			moreActionsButton = ui.MoreActionsButton;
			headerActionsMenu = ui.HeaderActionsMenu;
			settingsMenuItem = ui.SettingsMenuItem;
			clearTimelineMenuItem = ui.ClearTimelineMenuItem;
			closeMenuItem = ui.CloseMenuItem;
			endpointSummaryLabel = ui.EndpointSummaryLabel;
			connectionHintLabel = ui.ConnectionHintLabel;
			statusLabel = ui.StatusLabel;
			contentSplitContainer = ui.ContentSplitContainer;
			timelineHostPanel = ui.TimelineHostPanel;
			transcriptControl = ui.TranscriptControl;
			toolSelectorComboBox = ui.ToolSelectorComboBox;
			toolArgumentsTextBox = ui.ToolArgumentsTextBox;
			toolStateLabel = ui.ToolStateLabel;
			toolDescriptionLabel = ui.ToolDescriptionLabel;
			invokeToolButton = ui.InvokeToolButton;
			composerTextBox = ui.ComposerTextBox;
			composerStateLabel = ui.ComposerStateLabel;
			composerBusyIndicatorLabel = ui.ComposerBusyIndicatorLabel;
			sendButton = ui.SendButton;
			modeToggleButton = ui.ModeToggleButton;
			titleRowLayout = ui.TitleRowLayout;
			titleBarLayout = ui.TitleBarLayout;
			headerActionPanel = ui.HeaderActionPanel;
			workspaceHintLabel = ui.WorkspaceHintLabel;
			toolHeaderLayout = ui.ToolHeaderLayout;
			toolActionBar = ui.ToolActionBar;
			toolActionHintLabel = ui.ToolActionHintLabel;
			composerFooterLayout = ui.ComposerFooterLayout;
			composerModelLabel = ui.ComposerModelLabel;
			composerBusyIndicatorLabel = ui.ComposerBusyIndicatorLabel;
			actionToolTip = new ToolTip();
			composerBusyTimer = new Timer { Interval = 120 };
			composerBusyTimer.Tick += ComposerBusyTimer_Tick;
			toolSuggestMenu = new ContextMenuStrip { ShowImageMargin = false };
			composerTextBox.TextChanged += (_, _) => RefreshToolSuggestMenu();
			composerTextBox.Leave += (_, _) => toolSuggestMenu.Hide();
			transcriptControl.ApproveOnceClicked += (_, _) => _ = RunControllerActionAsync(() => controller.ApprovePendingToolInvocationAsync(false));
			transcriptControl.ApproveAlwaysClicked += (_, _) => _ = RunControllerActionAsync(() => controller.ApprovePendingToolInvocationAsync(true));
			transcriptControl.DeclineClicked += (_, _) => controller.DeclinePendingToolInvocation();
			contentSplitContainer.Resize += ContentSplitContainer_Resize;
			contentSplitContainer.SplitterMoved += ContentSplitContainer_SplitterMoved;
			Resize += (_, _) => ApplyResponsiveLayout();

			Controls.Add(ui.RootLayout);

			controller.StateChanged += Controller_StateChanged;
			_ = controller.RefreshAvailableModelsAsync();
			ApplyResponsiveLayout();
			EnsureContentSplitInitialized();
			RefreshView();
		}

		McpSidecarUi CreateUi(Color backgroundColor, string sidecarTitle)
		{
			var ui = new McpSidecarUi();
			var rootLayout = CreateRootLayout(backgroundColor);
			var headerCard = CreateHeaderCard(ui, sidecarTitle);
			var timelineSectionPanel = CreateTimelineSection(ui);
			var workspaceTabs = CreateWorkspaceTabs(ui, backgroundColor);

			ui.ContentSplitContainer = CreateContentSplitContainer(backgroundColor, timelineSectionPanel, workspaceTabs);
			rootLayout.Controls.Add(headerCard, 0, 0);
			rootLayout.Controls.Add(ui.ContentSplitContainer, 0, 1);
			ui.RootLayout = rootLayout;
			return ui;
		}

		static TableLayoutPanel CreateRootLayout(Color backgroundColor)
		{
			var rootLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				BackColor = backgroundColor,
			};
			rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			return rootLayout;
		}

		Panel CreateHeaderCard(McpSidecarUi ui, string sidecarTitle)
		{
			var headerCard = new Panel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(10),
				Margin = new Padding(0, 0, 0, 8),
			};

			var headerLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				AutoSize = true,
				BackColor = Color.White,
			};
			headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var titleRowLayout = CreateTitleRow(ui, sidecarTitle);
			var titleBarLayout = CreateTitleBar(ui);
			var summaryBarLayout = CreateSummaryBar(ui);

			headerLayout.Controls.Add(titleRowLayout, 0, 0);
			headerLayout.Controls.Add(titleBarLayout, 0, 1);
			headerLayout.Controls.Add(summaryBarLayout, 0, 2);
			headerCard.Controls.Add(headerLayout);
			return headerCard;
		}

		static TableLayoutPanel CreateTitleRow(McpSidecarUi ui, string sidecarTitle)
		{
			var titleRowLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 2,
				AutoSize = true,
				Margin = new Padding(0, 0, 0, 6),
				BackColor = Color.White,
			};
			titleRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			titleRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			var titleTextLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				AutoSize = true,
				BackColor = Color.White,
			};
			titleTextLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			titleTextLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			ui.TitleLabel = new Label
			{
				Text = sidecarTitle,
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Font = new Font("Segoe UI", 13.25F, FontStyle.Bold),
				Margin = new Padding(0),
				ForeColor = Color.FromArgb(45, 39, 34),
			};

			var titleDescriptionLabel = new Label
			{
				AutoSize = true,
				Text = "Model-guided assistance with a chat-style MCP workspace.",
				Font = new Font("Segoe UI", 9F, FontStyle.Regular),
				ForeColor = Color.FromArgb(95, 83, 71),
				Margin = new Padding(0, 4, 0, 0),
			};

			ui.StatusLabel = new Label
			{
				AutoSize = true,
				Anchor = AnchorStyles.Top,
				Font = new Font("Segoe UI", 8F, FontStyle.Bold),
				Padding = new Padding(8, 4, 8, 4),
				BorderStyle = BorderStyle.None,
				Margin = new Padding(12, 2, 0, 0),
			};

			titleTextLayout.Controls.Add(ui.TitleLabel, 0, 0);
			titleTextLayout.Controls.Add(titleDescriptionLabel, 0, 1);
			titleRowLayout.Controls.Add(titleTextLayout, 0, 0);
			titleRowLayout.Controls.Add(ui.StatusLabel, 1, 0);
			ui.TitleRowLayout = titleRowLayout;
			return titleRowLayout;
		}

		TableLayoutPanel CreateTitleBar(McpSidecarUi ui)
		{
			var titleBarLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 2,
				AutoSize = true,
				BackColor = Color.White,
				Margin = new Padding(0, 0, 0, 8),
			};
			titleBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			titleBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			var workspaceHintLabel = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Text = "Use MCP when needed. Retry reconnects. More opens settings and conversation actions.",
				Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
				ForeColor = Color.FromArgb(95, 83, 71),
				Margin = new Padding(0, 4, 12, 0),
			};

			var actionPanel = CreateHeaderActionPanel(ui);
			titleBarLayout.Controls.Add(workspaceHintLabel, 0, 0);
			titleBarLayout.Controls.Add(actionPanel, 1, 0);
			ui.TitleBarLayout = titleBarLayout;
			ui.WorkspaceHintLabel = workspaceHintLabel;
			return titleBarLayout;
		}

		FlowLayoutPanel CreateHeaderActionPanel(McpSidecarUi ui)
		{
			ui.ConnectButton = CreateActionButton("Connect", isPrimary: true);
			ui.ConnectButton.Click += (_, _) => _ = RunControllerActionAsync(controller.ConnectAsync);

			ui.MoreActionsButton = CreateActionButton("More");
			ui.MoreActionsButton.MinimumSize = new Size(74, 34);

			ui.HeaderActionsMenu = new ContextMenuStrip();
			ui.SettingsMenuItem = new ToolStripMenuItem("Settings...");
			ui.SettingsMenuItem.Click += SettingsButton_Click;
			ui.ClearTimelineMenuItem = new ToolStripMenuItem("Clear Conversation...");
			ui.ClearTimelineMenuItem.Click += ClearTimelineButton_Click;
			ui.CloseMenuItem = new ToolStripMenuItem("Close Panel");
			ui.CloseMenuItem.Click += (_, _) => this.closeSidecar();
			ui.HeaderActionsMenu.Items.Add(ui.SettingsMenuItem);
			ui.HeaderActionsMenu.Items.Add(ui.ClearTimelineMenuItem);
			ui.HeaderActionsMenu.Items.Add(new ToolStripSeparator());
			ui.HeaderActionsMenu.Items.Add(ui.CloseMenuItem);
			ui.MoreActionsButton.Click += (_, _) => ui.HeaderActionsMenu.Show(ui.MoreActionsButton, new Point(0, ui.MoreActionsButton.Height));

			var actionPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				WrapContents = false,
				FlowDirection = FlowDirection.LeftToRight,
				Anchor = AnchorStyles.Right,
				Margin = new Padding(8, 0, 0, 0),
				BackColor = Color.White,
			};
			actionPanel.Controls.Add(ui.ConnectButton);
			actionPanel.Controls.Add(ui.MoreActionsButton);
			ui.HeaderActionPanel = actionPanel;
			return actionPanel;
		}

		static TableLayoutPanel CreateSummaryBar(McpSidecarUi ui)
		{
			ui.EndpointSummaryLabel = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
				ForeColor = Color.FromArgb(70, 62, 54),
				Margin = new Padding(0),
				MaximumSize = new Size(680, 0),
			};

			ui.ConnectionHintLabel = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 8.75F, FontStyle.Regular),
				ForeColor = Color.FromArgb(106, 92, 78),
				Margin = new Padding(0, 6, 0, 0),
				MaximumSize = new Size(760, 0),
			};

			var summaryBarLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 1,
				RowCount = 2,
				AutoSize = true,
				BackColor = Color.FromArgb(248, 246, 242),
				Padding = new Padding(12, 10, 12, 10),
				Margin = new Padding(0),
			};
			summaryBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			summaryBarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			summaryBarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			summaryBarLayout.Controls.Add(ui.EndpointSummaryLabel, 0, 0);
			summaryBarLayout.Controls.Add(ui.ConnectionHintLabel, 0, 1);
			return summaryBarLayout;
		}

		static Panel CreateTimelineSection(McpSidecarUi ui)
		{
			var timelineSectionPanel = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(0),
				Margin = new Padding(0),
			};

			var timelineSectionLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				BackColor = Color.White,
			};
			timelineSectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			timelineSectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			var timelineHeaderLayout = CreateTimelineHeader();
			ui.TimelineHostPanel = CreateTimelineHostPanel(ui);

			timelineSectionLayout.Controls.Add(timelineHeaderLayout, 0, 0);
			timelineSectionLayout.Controls.Add(ui.TimelineHostPanel, 0, 1);
			timelineSectionPanel.Controls.Add(timelineSectionLayout);
			return timelineSectionPanel;
		}

		static TableLayoutPanel CreateTimelineHeader()
		{
			var timelineHeaderLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 1,
				RowCount = 2,
				AutoSize = true,
				BackColor = Color.White,
				Padding = new Padding(14, 14, 14, 10),
				Margin = new Padding(0),
			};

			var timelineTitleLabel = new Label
			{
				AutoSize = true,
				Text = "Conversation",
				Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(49, 43, 38),
				Margin = new Padding(0),
			};

			var timelineHintLabel = new Label
			{
				AutoSize = true,
				Text = "Messages, tool steps, approvals, and errors appear here as one working thread.",
				Font = new Font("Segoe UI", 8.75F, FontStyle.Regular),
				ForeColor = Color.FromArgb(96, 85, 74),
				Margin = new Padding(0, 6, 0, 0),
			};

			timelineHeaderLayout.Controls.Add(timelineTitleLabel, 0, 0);
			timelineHeaderLayout.Controls.Add(timelineHintLabel, 0, 1);
			return timelineHeaderLayout;
		}

		static Panel CreateTimelineHostPanel(McpSidecarUi ui)
		{
			ui.TranscriptControl = new McpChatTranscriptControl
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(248, 246, 242),
			};

			var timelineHostPanel = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(248, 246, 242),
				Padding = new Padding(1, 0, 1, 1),
			};
			timelineHostPanel.Controls.Add(ui.TranscriptControl);
			return timelineHostPanel;
		}

		static SplitContainer CreateContentSplitContainer(Color backgroundColor, Control timelineSectionPanel, TabControl workspaceTabs)
		{
			var splitContainer = new SplitContainer
			{
				Dock = DockStyle.Fill,
				Orientation = Orientation.Horizontal,
				BackColor = backgroundColor,
				SplitterWidth = 8,
			};
			splitContainer.Panel1.Controls.Add(timelineSectionPanel);
			splitContainer.Panel2.Controls.Add(workspaceTabs);
			return splitContainer;
		}

		void ContentSplitContainer_Resize(object? sender, EventArgs e)
		{
			EnsureContentSplitInitialized();
		}

		void ContentSplitContainer_SplitterMoved(object? sender, SplitterEventArgs e)
		{
			hasInitializedContentSplit = true;
		}

		TabControl CreateWorkspaceTabs(McpSidecarUi ui, Color backgroundColor)
		{
			ui.ModelSelectorComboBox = CreateModelSelectorComboBox();

			var workspaceTabs = new TabControl
			{
				Dock = DockStyle.Fill,
				Padding = new Point(18, 8),
			};

			var composerPanel = CreateComposerPanel(ui);
			var toolsPanel = CreateToolsPanel(ui);

			var promptTabPage = new TabPage("Prompt")
			{
				BackColor = backgroundColor,
				Padding = new Padding(0),
			};
			promptTabPage.Controls.Add(composerPanel);

			var toolsTabPage = new TabPage("Tools")
			{
				BackColor = backgroundColor,
				Padding = new Padding(0),
			};
			toolsTabPage.Controls.Add(toolsPanel);

			workspaceTabs.TabPages.Add(promptTabPage);
			workspaceTabs.TabPages.Add(toolsTabPage);
			return workspaceTabs;
		}

		ComboBox CreateModelSelectorComboBox()
		{
			var comboBox = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Dock = DockStyle.Fill,
				DisplayMember = nameof(AssistantModelDescriptor.DisplayName),
				Margin = new Padding(0),
				IntegralHeight = false,
			};
			comboBox.SelectedIndexChanged += ModelSelectorComboBox_SelectedIndexChanged;
			return comboBox;
		}

		Panel CreateToolsPanel(McpSidecarUi ui)
		{
			var toolsPanel = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				Padding = new Padding(16),
				BorderStyle = BorderStyle.FixedSingle,
				Margin = new Padding(0),
			};

			var toolsLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 8,
				BackColor = Color.White,
			};
			toolsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			toolsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var toolHeaderLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 2,
				AutoSize = true,
				BackColor = Color.White,
				Margin = new Padding(0, 0, 0, 10),
			};
			toolHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			toolHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			var toolTitleLabel = new Label
			{
				AutoSize = true,
				Text = "MCP Tool",
				Font = new Font("Segoe UI", 10F, FontStyle.Bold),
				ForeColor = Color.FromArgb(48, 42, 37),
				Margin = new Padding(0),
			};

			ui.ToolStateLabel = new Label
			{
				AutoSize = true,
				Anchor = AnchorStyles.Right,
				Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
				ForeColor = Color.FromArgb(96, 85, 74),
				Margin = new Padding(12, 2, 0, 0),
			};

			toolHeaderLayout.Controls.Add(toolTitleLabel, 0, 0);
			toolHeaderLayout.Controls.Add(ui.ToolStateLabel, 1, 0);
			ui.ToolHeaderLayout = toolHeaderLayout;
			toolsLayout.Controls.Add(toolHeaderLayout, 0, 0);

			var toolHintLabel = new Label
			{
				Text = "Connect to load the endpoint tool catalog, then choose a tool and provide JSON arguments for a direct MCP call.",
				AutoSize = true,
				Dock = DockStyle.Fill,
				ForeColor = Color.FromArgb(92, 82, 71),
				Margin = new Padding(0, 0, 0, 10),
			};
			toolsLayout.Controls.Add(toolHintLabel, 0, 1);

			var toolFieldLabel = new Label
			{
				AutoSize = true,
				Text = "Tool",
				Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(95, 83, 71),
				Margin = new Padding(0, 0, 0, 6),
			};
			toolsLayout.Controls.Add(toolFieldLabel, 0, 2);

			ui.ToolSelectorComboBox = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Dock = DockStyle.Fill,
				DisplayMember = nameof(McpToolDescriptor.Name),
				Margin = new Padding(0, 0, 0, 10),
				IntegralHeight = false,
			};
			ui.ToolSelectorComboBox.SelectedIndexChanged += ToolSelectorComboBox_SelectedIndexChanged;
			toolsLayout.Controls.Add(ui.ToolSelectorComboBox, 0, 3);

			ui.ToolDescriptionLabel = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				ForeColor = Color.FromArgb(92, 82, 71),
				BackColor = Color.FromArgb(249, 246, 240),
				BorderStyle = BorderStyle.FixedSingle,
				Padding = new Padding(12, 10, 12, 10),
				Margin = new Padding(0, 0, 0, 10),
				MaximumSize = new Size(760, 0),
			};
			toolsLayout.Controls.Add(ui.ToolDescriptionLabel, 0, 4);

			var toolArgumentsLabel = new Label
			{
				AutoSize = true,
				Text = "Arguments (JSON)",
				Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(95, 83, 71),
				Margin = new Padding(0, 0, 0, 6),
			};
			toolsLayout.Controls.Add(toolArgumentsLabel, 0, 5);

			ui.ToolArgumentsTextBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ScrollBars = ScrollBars.Vertical,
				MinimumSize = new Size(0, 160),
				Text = controller.ToolArgumentsJson,
				Font = new Font("Consolas", 8.75F, FontStyle.Regular),
				BorderStyle = BorderStyle.FixedSingle,
				WordWrap = false,
			};
			ui.ToolArgumentsTextBox.TextChanged += ToolArgumentsTextBox_TextChanged;

			ui.InvokeToolButton = new Button
			{
				AutoSize = false,
				Text = "Run Tool",
				MinimumSize = new Size(104, 40),
				Size = new Size(118, 40),
				Anchor = AnchorStyles.Right,
				BackColor = Color.FromArgb(43, 93, 71),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(12, 0, 0, 0),
			};
			ui.InvokeToolButton.FlatAppearance.BorderSize = 0;
			ui.InvokeToolButton.Click += (_, _) => _ = RunControllerActionAsync(controller.InvokeSelectedToolAsync, lockPromptEditing: false);

			var toolActionBar = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 1,
				AutoSize = true,
				BackColor = Color.White,
				Margin = new Padding(0, 12, 0, 0),
			};
			toolActionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			toolActionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			var toolActionHintLabel = new Label
			{
				AutoSize = true,
				Dock = DockStyle.Fill,
				Text = "Run the selected tool with the JSON payload above.",
				Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
				ForeColor = Color.FromArgb(96, 85, 74),
				Margin = new Padding(0, 11, 12, 0),
			};

			toolsLayout.Controls.Add(ui.ToolArgumentsTextBox, 0, 6);
			toolActionBar.Controls.Add(toolActionHintLabel, 0, 0);
			toolActionBar.Controls.Add(ui.InvokeToolButton, 1, 0);
			ui.ToolActionBar = toolActionBar;
			ui.ToolActionHintLabel = toolActionHintLabel;
			toolsLayout.Controls.Add(toolActionBar, 0, 7);
			toolsPanel.Controls.Add(toolsLayout);
			return toolsPanel;
		}

		Panel CreateComposerPanel(McpSidecarUi ui)
		{
			var composerHeaderLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 1,
				AutoSize = true,
				BackColor = Color.White,
				Margin = new Padding(0, 0, 0, 10),
			};
			composerHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

			var composerTitleLabel = new Label
			{
				AutoSize = true,
				Text = "Prompt",
				Font = new Font("Segoe UI", 10F, FontStyle.Bold),
				ForeColor = Color.FromArgb(48, 42, 37),
				Margin = new Padding(0),
			};

			ui.ComposerStateLabel = new Label
			{
				AutoSize = true,
				Anchor = AnchorStyles.Right,
				Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
				ForeColor = Color.FromArgb(96, 85, 74),
				Margin = new Padding(12, 2, 0, 0),
			};

			composerHeaderLayout.Controls.Add(composerTitleLabel, 0, 0);

			ui.ModeToggleButton = new Button
			{
				AutoSize = true,
				Text = "Act",
				MinimumSize = new Size(58, 34),
				Anchor = AnchorStyles.Right,
				BackColor = Color.FromArgb(74, 98, 126),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
				Padding = new Padding(8, 0, 8, 0),
				Margin = new Padding(8, 0, 0, 0),
			};
			ui.ModeToggleButton.FlatAppearance.BorderSize = 0;
			ui.ModeToggleButton.Click += (_, _) => controller.SetAssistantMode(
				controller.AssistantMode == McpAssistantMode.Chat ? McpAssistantMode.Act : McpAssistantMode.Chat);

			var composerPanel = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = Color.White,
				Padding = new Padding(16),
				BorderStyle = BorderStyle.FixedSingle,
				Margin = new Padding(0),
			};

			var composerLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				BackColor = Color.White,
			};
			composerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			composerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			composerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			composerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			composerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var composerHintLabel = new Label
			{
				Text = "Ask for workflow guidance, app inspection, or a direct tool action. Results and approvals stay in the conversation above.",
				AutoSize = true,
				Dock = DockStyle.Fill,
				ForeColor = Color.FromArgb(92, 82, 71),
				Margin = new Padding(0, 0, 0, 10),
			};
			composerLayout.Controls.Add(composerHeaderLayout, 0, 0);
			composerLayout.Controls.Add(composerHintLabel, 0, 1);

			ui.ComposerTextBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ScrollBars = ScrollBars.Vertical,
				MinimumSize = new Size(0, 132),
				PlaceholderText = "Describe the next task, question, or workflow step.",
				Font = new Font("Segoe UI", 9F, FontStyle.Regular),
				BorderStyle = BorderStyle.FixedSingle,
			};
			ui.ComposerTextBox.TextChanged += (_, _) => RefreshComposerState();
			ui.ComposerTextBox.KeyDown += ComposerTextBox_KeyDown;

			var composerFooterLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 6,
				RowCount = 1,
				AutoSize = true,
				BackColor = Color.White,
				Margin = new Padding(0, 12, 0, 0),
			};
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

			var composerModelLabel = new Label
			{
				AutoSize = true,
				Text = "Model",
				Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
				ForeColor = Color.FromArgb(95, 83, 71),
				Margin = new Padding(0, 11, 8, 0),
			};

			ui.ModelSelectorComboBox.Dock = DockStyle.None;
			ui.ModelSelectorComboBox.Anchor = AnchorStyles.Left;
			ui.ModelSelectorComboBox.Margin = new Padding(0, 6, 12, 0);
			ui.ModelSelectorComboBox.MinimumSize = new Size(168, 0);
			ui.ModelSelectorComboBox.Width = 168;

			ui.ComposerBusyIndicatorLabel = new Label
			{
				AutoSize = false,
				Text = ComposerBusyFrames[0],
				TextAlign = ContentAlignment.MiddleCenter,
				Size = new Size(20, 18),
				MinimumSize = new Size(20, 18),
				Font = new Font("Consolas", 9F, FontStyle.Bold),
				ForeColor = Color.FromArgb(74, 98, 126),
				Anchor = AnchorStyles.Right,
				Margin = new Padding(12, 8, 0, 0),
				Visible = false,
			};

			ui.SendButton = new Button
			{
				AutoSize = false,
				Text = "Send",
				MinimumSize = new Size(92, 38),
				Size = new Size(92, 38),
				Anchor = AnchorStyles.Right,
				BackColor = Color.FromArgb(74, 98, 126),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(12, 0, 0, 0),
			};
			ui.SendButton.FlatAppearance.BorderSize = 0;
			ui.SendButton.Click += (_, _) => _ = HandleSendButtonClickAsync();

			composerLayout.Controls.Add(ui.ComposerTextBox, 0, 2);
			composerFooterLayout.Controls.Add(composerModelLabel, 0, 0);
			composerFooterLayout.Controls.Add(ui.ModelSelectorComboBox, 1, 0);
			composerFooterLayout.Controls.Add(ui.ComposerStateLabel, 3, 0);
			composerFooterLayout.Controls.Add(ui.ComposerBusyIndicatorLabel, 4, 0);
			composerFooterLayout.Controls.Add(ui.ModeToggleButton, 5, 0);
			composerFooterLayout.Controls.Add(ui.SendButton, 6, 0);
			ui.ComposerFooterLayout = composerFooterLayout;
			ui.ComposerModelLabel = composerModelLabel;
			composerLayout.Controls.Add(composerFooterLayout, 0, 3);
			composerPanel.Controls.Add(composerLayout);
			return composerPanel;
		}

		sealed class McpSidecarUi
		{
			public TableLayoutPanel RootLayout { get; set; } = null!;
			public TableLayoutPanel TitleRowLayout { get; set; } = null!;
			public TableLayoutPanel TitleBarLayout { get; set; } = null!;
			public FlowLayoutPanel HeaderActionPanel { get; set; } = null!;
			public Label WorkspaceHintLabel { get; set; } = null!;
			public Label TitleLabel { get; set; } = null!;
			public ComboBox ModelSelectorComboBox { get; set; } = null!;
			public Button ConnectButton { get; set; } = null!;
			public Button MoreActionsButton { get; set; } = null!;
			public ContextMenuStrip HeaderActionsMenu { get; set; } = null!;
			public ToolStripMenuItem SettingsMenuItem { get; set; } = null!;
			public ToolStripMenuItem ClearTimelineMenuItem { get; set; } = null!;
			public ToolStripMenuItem CloseMenuItem { get; set; } = null!;
			public Label EndpointSummaryLabel { get; set; } = null!;
			public Label ConnectionHintLabel { get; set; } = null!;
			public Label StatusLabel { get; set; } = null!;
			public SplitContainer ContentSplitContainer { get; set; } = null!;
			public Panel TimelineHostPanel { get; set; } = null!;
			public McpChatTranscriptControl TranscriptControl { get; set; } = null!;
			public ComboBox ToolSelectorComboBox { get; set; } = null!;
			public TextBox ToolArgumentsTextBox { get; set; } = null!;
			public TableLayoutPanel ToolHeaderLayout { get; set; } = null!;
			public Label ToolStateLabel { get; set; } = null!;
			public Label ToolDescriptionLabel { get; set; } = null!;
			public TableLayoutPanel ToolActionBar { get; set; } = null!;
			public Label ToolActionHintLabel { get; set; } = null!;
			public Button InvokeToolButton { get; set; } = null!;
			public TextBox ComposerTextBox { get; set; } = null!;
			public TableLayoutPanel ComposerFooterLayout { get; set; } = null!;
			public Label ComposerModelLabel { get; set; } = null!;
			public Label ComposerStateLabel { get; set; } = null!;
			public Label ComposerBusyIndicatorLabel { get; set; } = null!;
			public Button SendButton { get; set; } = null!;
			public Button ModeToggleButton { get; set; } = null!;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				controller.StateChanged -= Controller_StateChanged;
				composerBusyTimer.Stop();
				composerBusyTimer.Dispose();
				actionToolTip.Dispose();
				headerActionsMenu.Dispose();
				moreActionsButton.Dispose();
				contentSplitContainer.Dispose();
				composerTextBox.Dispose();
				toolArgumentsTextBox.Dispose();
				toolDescriptionLabel.Dispose();
				toolStateLabel.Dispose();
				invokeToolButton.Dispose();
				toolSelectorComboBox.Dispose();
				connectionHintLabel.Dispose();
				composerStateLabel.Dispose();
				composerBusyIndicatorLabel.Dispose();
				sendButton.Dispose();
				modeToggleButton.Dispose();
				toolSuggestMenu.Dispose();
				transcriptControl.Dispose();
				timelineHostPanel.Dispose();
				statusLabel.Dispose();
				endpointSummaryLabel.Dispose();
				connectButton.Dispose();
				modelSelectorComboBox.Dispose();
				titleLabel.Dispose();
			}

			base.Dispose(disposing);
		}

		void SettingsButton_Click(object? sender, EventArgs e)
		{
			using var settingsForm = new McpSidecarSettingsForm(
				controller.EndpointOverride,
				controller.AutoConnectOnStartup,
				controller.ResolvedEndpoint,
				controller.ResolutionFailureReason);

			if (settingsForm.ShowDialog(FindForm()) == DialogResult.OK)
			{
				controller.UpdateConnectionPreferences(settingsForm.EndpointOverride, settingsForm.AutoConnectOnStartup);
			}
		}

		void ClearTimelineButton_Click(object? sender, EventArgs e)
		{
			if (!HasConversationToClear())
			{
				return;
			}

			var confirmation = MessageBox.Show(
				this,
				"Clear the sidecar conversation history? This removes the local thread and cannot be undone.",
				"Clear Conversation",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning,
				MessageBoxDefaultButton.Button2);

			if (confirmation == DialogResult.Yes)
			{
				controller.ClearTranscript();
			}
		}

		void Controller_StateChanged(object? sender, EventArgs e)
		{
			if (IsDisposed)
			{
				return;
			}

			if (InvokeRequired)
			{
				BeginInvoke((MethodInvoker)RefreshView);
				return;
			}

			RefreshView();
		}

		void ModelSelectorComboBox_SelectedIndexChanged(object? sender, EventArgs e)
		{
			if (isRefreshingView)
			{
				return;
			}

			controller.SelectModel((modelSelectorComboBox.SelectedItem as AssistantModelDescriptor)?.Id);
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("SaasCopilotOne", "CW1046:Do Not Specify Tooltips Manually Rule", Justification = "<Pending>")]
		void RefreshView()
		{
			isRefreshingView = true;

			try
			{
				RefreshModelSelection();
				endpointSummaryLabel.Text = BuildEndpointSummary();
				endpointSummaryLabel.Visible = !string.IsNullOrWhiteSpace(endpointSummaryLabel.Text);
				statusLabel.Text = BuildStatusBadgeText();
				statusLabel.BackColor = GetStatusBackground(controller.ConnectionState);
				statusLabel.ForeColor = GetStatusForeground(controller.ConnectionState);
				connectionHintLabel.Text = BuildConnectionHintText();
				if (connectionHintLabel.Parent is Control summaryPanel)
				{
					summaryPanel.BackColor = GetSummaryBackground(controller.ConnectionState);
				}

				connectButton.Text = BuildConnectButtonText();
				connectButton.Enabled = !isRunningAction
					&& controller.ResolvedEndpoint is not null
					&& controller.ConnectionState is not McpConnectionState.Connecting
					&& controller.ConnectionState is not McpConnectionState.GeneratingResponse;

				moreActionsButton.Enabled = true;
				settingsMenuItem.Enabled = !isRunningAction;
				clearTimelineMenuItem.Enabled = !isRunningAction && HasConversationToClear();
				closeMenuItem.Enabled = true;
				actionToolTip.SetToolTip(connectButton, BuildConnectButtonToolTip());
				actionToolTip.SetToolTip(moreActionsButton, "Open settings, clear conversation, or close the sidecar.");
				modelSelectorComboBox.Enabled = !isRunningAction && modelSelectorComboBox.Items.Count > 0;
				RefreshToolSelection();
				RefreshToolState();

				composerTextBox.PlaceholderText = controller.AssistantMode == McpAssistantMode.Chat
					? "Chat mode: the model will answer directly without calling MCP tools."
					: $"Act mode: use #toolName to invoke a tool. Selected: {controller.SelectedModel?.DisplayName}.";
				composerTextBox.Enabled = true;
				RefreshComposerState();
				RefreshModeToggle();
				RefreshTimeline();
			}
			finally
			{
				isRefreshingView = false;
			}
		}

		void EnsureContentSplitInitialized()
		{
			if (contentSplitContainer.Height <= 0)
			{
				return;
			}

			var availableHeight = Math.Max(0, contentSplitContainer.Height - contentSplitContainer.SplitterWidth);
			if (availableHeight <= 0)
			{
				return;
			}

			var effectivePanel1MinSize = Math.Min(PreferredContentPanelMinSize, availableHeight / 2);
			var effectivePanel2MinSize = Math.Min(PreferredContentPanelMinSize, Math.Max(0, availableHeight - effectivePanel1MinSize));
			contentSplitContainer.Panel1MinSize = effectivePanel1MinSize;
			contentSplitContainer.Panel2MinSize = effectivePanel2MinSize;

			var minimumTopHeight = effectivePanel1MinSize;
			var maximumTopHeight = Math.Max(minimumTopHeight, availableHeight - effectivePanel2MinSize);
			var desiredTopHeight = hasInitializedContentSplit
				? contentSplitContainer.SplitterDistance
				: (int)Math.Round(availableHeight * 0.68, MidpointRounding.AwayFromZero);
			var clampedTopHeight = Math.Min(Math.Max(desiredTopHeight, minimumTopHeight), maximumTopHeight);

			if (contentSplitContainer.SplitterDistance != clampedTopHeight)
			{
				contentSplitContainer.SplitterDistance = clampedTopHeight;
			}

			hasInitializedContentSplit = true;
		}

		void RefreshModelSelection()
		{
			modelSelectorComboBox.BeginUpdate();
			modelSelectorComboBox.Items.Clear();
			foreach (var model in controller.AvailableModels)
			{
				modelSelectorComboBox.Items.Add(model);
			}
			modelSelectorComboBox.EndUpdate();

			for (var index = 0; index < modelSelectorComboBox.Items.Count; index++)
			{
				if (modelSelectorComboBox.Items[index] is AssistantModelDescriptor model
					&& string.Equals(model.Id, controller.SelectedModel?.Id, StringComparison.Ordinal))
				{
					modelSelectorComboBox.SelectedIndex = index;
					return;
				}
			}

			if (modelSelectorComboBox.Items.Count > 0)
			{
				modelSelectorComboBox.SelectedIndex = 0;
			}
		}

		void RefreshToolSelection()
		{
			var selectedToolName = controller.SelectedToolName;
			toolSelectorComboBox.BeginUpdate();
			toolSelectorComboBox.Items.Clear();
			foreach (var tool in controller.AvailableTools)
			{
				toolSelectorComboBox.Items.Add(tool);
			}
			toolSelectorComboBox.EndUpdate();

			for (var index = 0; index < toolSelectorComboBox.Items.Count; index++)
			{
				if (toolSelectorComboBox.Items[index] is McpToolDescriptor tool
					&& string.Equals(tool.Name, selectedToolName, StringComparison.Ordinal))
				{
					toolSelectorComboBox.SelectedIndex = index;
					toolArgumentsTextBox.Text = controller.ToolArgumentsJson;
					return;
				}
			}

			if (toolSelectorComboBox.Items.Count > 0)
			{
				toolSelectorComboBox.SelectedIndex = 0;
			}
			else if (toolArgumentsTextBox.Text != controller.ToolArgumentsJson)
			{
				toolArgumentsTextBox.Text = controller.ToolArgumentsJson;
			}
		}

		void RefreshToolState()
		{
			var selectedTool = toolSelectorComboBox.SelectedItem as McpToolDescriptor;
			toolSelectorComboBox.Enabled = !isRunningAction && controller.AvailableTools.Count > 0;
			toolArgumentsTextBox.Enabled = controller.AvailableTools.Count > 0;
			toolDescriptionLabel.Text = selectedTool is null
				? "No MCP tools are currently loaded for the resolved endpoint."
				: BuildToolDescription(selectedTool);
			toolStateLabel.Text = BuildToolStateText();
			ApplyPrimaryButtonState(
				invokeToolButton,
				CanInvokeSelectedTool(selectedTool),
				Color.FromArgb(43, 93, 71),
				Color.FromArgb(130, 155, 144));
		}

		void RefreshToolSuggestMenu()
		{
			var text = composerTextBox.Text;
			var caretPos = composerTextBox.SelectionStart;
			var wordStart = caretPos;
			while (wordStart > 0 && text[wordStart - 1] != ' ' && text[wordStart - 1] != '\n' && text[wordStart - 1] != '\r')
			{
				wordStart--;
			}

			var word = caretPos >= wordStart ? text[wordStart..caretPos] : string.Empty;
			if (!word.StartsWith("#", StringComparison.Ordinal))
			{
				toolSuggestMenu.Hide();
				return;
			}

			var filter = word[1..];
			toolSuggestMenu.Items.Clear();
			foreach (var tool in controller.AvailableTools)
			{
				if (!tool.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var capturedStart = wordStart;
				var capturedEnd = caretPos;
				var capturedName = tool.Name;
				var item = new ToolStripMenuItem(tool.Name);
				if (!string.IsNullOrWhiteSpace(tool.Description))
				{
					item.ToolTipText = tool.Description.Length <= 80 ? tool.Description : tool.Description[..77] + "...";
				}

				item.Click += (_, _) => InsertToolMention(capturedStart, capturedEnd, capturedName);
				toolSuggestMenu.Items.Add(item);
			}

			if (toolSuggestMenu.Items.Count == 0)
			{
				toolSuggestMenu.Hide();
				return;
			}

			var caretLocal = composerTextBox.GetPositionFromCharIndex(wordStart);
			var screenPt = composerTextBox.PointToScreen(new Point(caretLocal.X, caretLocal.Y + Font.Height + 2));
			toolSuggestMenu.Show(screenPt);
		}

		void InsertToolMention(int wordStart, int wordEnd, string toolName)
		{
			var text = composerTextBox.Text;
			var insertion = "#" + toolName + " ";
			var suffix = wordEnd < text.Length ? text[wordEnd..] : string.Empty;
			composerTextBox.Text = text[..wordStart] + insertion + suffix;
			composerTextBox.SelectionStart = wordStart + insertion.Length;
			composerTextBox.SelectionLength = 0;
		}

		void RefreshModeToggle()
		{
			var isActMode = controller.AssistantMode == McpAssistantMode.Act;
			modeToggleButton.Text = isActMode ? "Act" : "Chat";
			modeToggleButton.BackColor = isActMode ? Color.FromArgb(74, 98, 126) : Color.FromArgb(130, 95, 50);
			modeToggleButton.FlatAppearance.BorderColor = modeToggleButton.BackColor;
		}

		void RefreshTimeline()
		{
			transcriptControl.SetConversation(controller.TranscriptEntries, controller.PendingApprovalRequest);
		}

		void ComposerTextBox_KeyDown(object? sender, KeyEventArgs e)
		{
			if (ShouldSubmitPromptOnEnter(e))
			{
				e.SuppressKeyPress = true;
				e.Handled = true;
				_ = SubmitPromptAsync();
			}
		}

		static bool ShouldSubmitPromptOnEnter(KeyEventArgs e)
		{
			return e.KeyCode == Keys.Enter
				&& !e.Shift
				&& !e.Alt;
		}

		void RefreshComposerState()
		{
			composerStateLabel.Text = BuildComposerStateText();
			UpdateComposerBusyIndicator();
			canSubmitPrompt = CanSubmitPrompt();
			var canCancelCurrentOperation = controller.CanCancelCurrentOperation;
			sendButton.Text = canCancelCurrentOperation ? "Stop" : "Send";
			ApplyPrimaryButtonState(
				sendButton,
				canCancelCurrentOperation || canSubmitPrompt,
				canCancelCurrentOperation ? Color.FromArgb(146, 78, 62) : Color.FromArgb(74, 98, 126),
				Color.FromArgb(132, 145, 160));
		}

		async System.Threading.Tasks.Task SubmitPromptAsync()
		{
			if (!CanSubmitPrompt())
			{
				return;
			}

			var prompt = composerTextBox.Text;
			if (string.IsNullOrWhiteSpace(prompt))
			{
				return;
			}

			composerTextBox.Clear();
			var submitted = false;
			await RunControllerActionAsync(async () => submitted = await controller.SubmitPromptAsync(prompt), lockPromptEditing: false);
			if (!submitted)
			{
				composerTextBox.Text = prompt;
				composerTextBox.SelectionStart = composerTextBox.TextLength;
				composerTextBox.SelectionLength = 0;
			}

			composerTextBox.Focus();
		}

		async System.Threading.Tasks.Task HandleSendButtonClickAsync()
		{
			if (controller.CanCancelCurrentOperation)
			{
				controller.CancelCurrentOperation();
				return;
			}

			await SubmitPromptAsync();
		}

		async System.Threading.Tasks.Task RunControllerActionAsync(Func<System.Threading.Tasks.Task> action, bool lockPromptEditing = true)
		{
			if (isRunningAction)
			{
				return;
			}

			isRunningAction = true;
			RefreshView();

			try
			{
				if (lockPromptEditing)
				{
					composerTextBox.Enabled = false;
				}

				await action();
			}
			catch (HttpRequestException ex)
			{
				MessageBox.Show(this, ex.Message, "MCP Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (IOException ex)
			{
				MessageBox.Show(this, ex.Message, "MCP Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (InvalidOperationException ex)
			{
				MessageBox.Show(this, ex.Message, "MCP Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (JsonException ex)
			{
				MessageBox.Show(this, ex.Message, "MCP Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (NotSupportedException ex)
			{
				MessageBox.Show(this, ex.Message, "MCP Sidecar", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				isRunningAction = false;
				RefreshView();
			}
		}

		void ToolSelectorComboBox_SelectedIndexChanged(object? sender, EventArgs e)
		{
			if (isRefreshingView)
			{
				return;
			}

			controller.SelectTool((toolSelectorComboBox.SelectedItem as McpToolDescriptor)?.Name);
			RefreshToolState();
		}

		void ToolArgumentsTextBox_TextChanged(object? sender, EventArgs e)
		{
			if (isRefreshingView)
			{
				return;
			}

			controller.SetToolArgumentsJson(toolArgumentsTextBox.Text);
			RefreshToolState();
		}

		string BuildEndpointSummary()
		{
			if (controller.ResolvedEndpoint is null)
			{
				return controller.ConnectionState == McpConnectionState.Disabled
					? string.Empty
					: "No MCP endpoint is currently available.";
			}

			var authority = controller.ResolvedEndpoint.IsDefaultPort
				? controller.ResolvedEndpoint.Host
				: controller.ResolvedEndpoint.Authority;
			var path = controller.ResolvedEndpoint.AbsolutePath == "/" ? string.Empty : controller.ResolvedEndpoint.AbsolutePath;
			return $"Endpoint {authority}{path}";
		}

		string BuildConnectionHintText()
		{
			if (!string.IsNullOrWhiteSpace(controller.ActivityText)
				&& controller.ConnectionState is McpConnectionState.Connecting or McpConnectionState.GeneratingResponse)
			{
				return controller.ActivityText;
			}

			return controller.ConnectionState switch
			{
				McpConnectionState.Connected => controller.PendingApprovalRequest is null
					? "Connected and ready. Ask a question or submit a direct tool request from the prompt below."
					: "A tool approval is waiting in the conversation. Approve or decline it before sending another prompt.",
				McpConnectionState.Connecting => "Connecting to the MCP endpoint and refreshing the available tool catalog.",
				McpConnectionState.GeneratingResponse => "The assistant is processing the current request. New prompts can be prepared and sent once the response finishes.",
				McpConnectionState.ApprovalRequired => "A model-requested tool call needs confirmation before the workflow can continue.",
				McpConnectionState.EndpointUnresolved => "Open a SaasCopilot page or use More > Settings to provide an endpoint so the assistant can reach MCP.",
				McpConnectionState.DiscoveryFailed => "Tool discovery failed. Retry to rediscover tools, or use More > Settings to verify the endpoint.",
				McpConnectionState.ModelResponseFailed => "The model request failed. Review the conversation entry for the failure details and retry from the prompt box.",
				McpConnectionState.ToolInvocationFailed => "The MCP tool call failed. Inspect the tool result card and retry or adjust the request.",
				_ => "Choose a model and connect when you want MCP tools available to the assistant.",
			};
		}

		string BuildConnectButtonToolTip()
		{
			return controller.ConnectionState switch
			{
				McpConnectionState.DiscoveryFailed => "Retry connection and tool discovery.",
				McpConnectionState.Connected => "Reconnect to refresh tools and session state.",
				_ => "Connect to the current MCP endpoint.",
			};
		}

		string BuildStatusBadgeText()
		{
			return controller.ConnectionState switch
			{
				McpConnectionState.EndpointUnresolved => "Set endpoint",
				McpConnectionState.DiscoveryFailed => "Needs attention",
				McpConnectionState.ApprovalRequired => "Approval needed",
				McpConnectionState.GeneratingResponse => "Working",
				_ => controller.StatusText,
			};
		}

		string BuildConnectButtonText()
		{
			return controller.ConnectionState switch
			{
				McpConnectionState.Connected => "Reconnect",
				McpConnectionState.DiscoveryFailed => "Retry",
				McpConnectionState.Connecting => "Connecting",
				_ => "Connect",
			};
		}

		string BuildComposerStateText()
		{
			if (!string.IsNullOrWhiteSpace(controller.ActivityText)
				&& controller.ConnectionState is McpConnectionState.Connecting or McpConnectionState.GeneratingResponse)
			{
				return isRunningAction
					? controller.ActivityText + " Drafting stays available."
					: controller.ActivityText;
			}

			if (isRunningAction)
			{
				return "Working... You can keep editing the prompt.";
			}

			if (controller.PendingApprovalRequest is not null)
			{
				return "Approval required above. Drafting is still available.";
			}

			if (controller.ConnectionState is McpConnectionState.Connecting or McpConnectionState.GeneratingResponse)
			{
				return "Waiting for current step";
			}

			return "Enter to send. Shift+Enter for newline";
		}

		string BuildToolStateText()
		{
			if (!string.IsNullOrWhiteSpace(controller.ActivityText)
				&& controller.ConnectionState is McpConnectionState.Connecting or McpConnectionState.GeneratingResponse)
			{
				return controller.ActivityText;
			}

			if (controller.ConnectionState == McpConnectionState.Connected && controller.AvailableTools.Count > 0)
			{
				return $"{controller.AvailableTools.Count} tool(s) ready";
			}

			if (controller.ConnectionState == McpConnectionState.Connecting)
			{
				return "Loading tools...";
			}

			if (controller.ConnectionState == McpConnectionState.DiscoveryFailed)
			{
				return "Discovery failed";
			}

			return "Connect to browse tools";
		}

		bool CanSubmitPrompt()
		{
			return !isRefreshingView
				&& !isRunningAction
				&& !string.IsNullOrWhiteSpace(composerTextBox.Text)
				&& controller.ConnectionState is not McpConnectionState.Connecting
				&& controller.ConnectionState is not McpConnectionState.GeneratingResponse;
		}

		bool IsComposerBusy()
		{
			return controller.ConnectionState == McpConnectionState.GeneratingResponse;
		}

		void UpdateComposerBusyIndicator()
		{
			var isBusy = IsComposerBusy();
			composerBusyIndicatorLabel.Visible = isBusy;
			if (isBusy)
			{
				composerBusyIndicatorLabel.Text = ComposerBusyFrames[composerBusyFrameIndex % ComposerBusyFrames.Length];
				if (!composerBusyTimer.Enabled)
				{
					composerBusyTimer.Start();
				}

				return;
			}

			composerBusyTimer.Stop();
			composerBusyFrameIndex = 0;
			composerBusyIndicatorLabel.Text = ComposerBusyFrames[0];
		}

		void ComposerBusyTimer_Tick(object? sender, EventArgs e)
		{
			composerBusyFrameIndex = (composerBusyFrameIndex + 1) % ComposerBusyFrames.Length;
			composerBusyIndicatorLabel.Text = ComposerBusyFrames[composerBusyFrameIndex];
		}

		bool HasConversationToClear()
		{
			if (controller.PendingApprovalRequest is not null)
			{
				return true;
			}

			for (var index = 0; index < controller.TranscriptEntries.Count; index++)
			{
				var entry = controller.TranscriptEntries[index];
				if (!string.Equals(entry.Kind, "system", StringComparison.Ordinal)
					|| !string.Equals(entry.Title, "Transcript cleared", StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		bool CanInvokeSelectedTool(McpToolDescriptor? selectedTool)
		{
			return !isRefreshingView
				&& !isRunningAction
				&& controller.ConnectionState == McpConnectionState.Connected
				&& selectedTool is not null;
		}

		void ApplyResponsiveLayout()
		{
			var useStackedLayout = ClientSize.Width <= NarrowLayoutThreshold;
			ConfigureTitleRowLayout(useStackedLayout);
			ConfigureTitleBarLayout(useStackedLayout);
			ConfigureToolLayout(useStackedLayout);
			ConfigureComposerFooterLayout(useStackedLayout);
		}

		void ConfigureTitleRowLayout(bool useStackedLayout)
		{
			titleRowLayout.SuspendLayout();
			titleRowLayout.ColumnStyles.Clear();
			titleRowLayout.RowStyles.Clear();

			if (useStackedLayout)
			{
				titleRowLayout.ColumnCount = 1;
				titleRowLayout.RowCount = 2;
				titleRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				titleRowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleRowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleRowLayout.SetColumn(statusLabel, 0);
				titleRowLayout.SetRow(statusLabel, 1);
				statusLabel.Anchor = AnchorStyles.Left;
				statusLabel.Margin = new Padding(0, 8, 0, 0);
			}
			else
			{
				titleRowLayout.ColumnCount = 2;
				titleRowLayout.RowCount = 1;
				titleRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				titleRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleRowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleRowLayout.SetColumn(statusLabel, 1);
				titleRowLayout.SetRow(statusLabel, 0);
				statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
				statusLabel.Margin = new Padding(12, 2, 0, 0);
			}

			titleRowLayout.ResumeLayout();
		}

		void ConfigureTitleBarLayout(bool useStackedLayout)
		{
			titleBarLayout.SuspendLayout();
			titleBarLayout.ColumnStyles.Clear();
			titleBarLayout.RowStyles.Clear();

			if (useStackedLayout)
			{
				titleBarLayout.ColumnCount = 1;
				titleBarLayout.RowCount = 2;
				titleBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				titleBarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleBarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleBarLayout.SetColumn(workspaceHintLabel, 0);
				titleBarLayout.SetRow(workspaceHintLabel, 0);
				titleBarLayout.SetColumn(headerActionPanel, 0);
				titleBarLayout.SetRow(headerActionPanel, 1);
				workspaceHintLabel.Margin = new Padding(0, 8, 0, 0);
				headerActionPanel.Anchor = AnchorStyles.Left;
				headerActionPanel.Margin = new Padding(0, 8, 0, 0);
				headerActionPanel.MaximumSize = new Size(Math.Max(140, titleBarLayout.DisplayRectangle.Width), 0);
			}
			else
			{
				titleBarLayout.ColumnCount = 2;
				titleBarLayout.RowCount = 1;
				titleBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				titleBarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				titleBarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				titleBarLayout.SetColumn(workspaceHintLabel, 0);
				titleBarLayout.SetRow(workspaceHintLabel, 0);
				titleBarLayout.SetColumn(headerActionPanel, 1);
				titleBarLayout.SetRow(headerActionPanel, 0);
				workspaceHintLabel.Margin = new Padding(0, 4, 12, 0);
				headerActionPanel.Anchor = AnchorStyles.Right;
				headerActionPanel.Margin = new Padding(8, 0, 0, 0);
				headerActionPanel.MaximumSize = Size.Empty;
			}

			titleBarLayout.ResumeLayout();
		}

		void ConfigureToolLayout(bool useStackedLayout)
		{
			toolHeaderLayout.SuspendLayout();
			toolHeaderLayout.ColumnStyles.Clear();
			toolHeaderLayout.RowStyles.Clear();
			toolActionBar.SuspendLayout();
			toolActionBar.ColumnStyles.Clear();
			toolActionBar.RowStyles.Clear();

			if (useStackedLayout)
			{
				toolHeaderLayout.ColumnCount = 1;
				toolHeaderLayout.RowCount = 2;
				toolHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				toolHeaderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolHeaderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolHeaderLayout.SetColumn(toolStateLabel, 0);
				toolHeaderLayout.SetRow(toolStateLabel, 1);
				toolStateLabel.Anchor = AnchorStyles.Left;
				toolStateLabel.Margin = new Padding(0, 6, 0, 0);

				toolActionBar.ColumnCount = 1;
				toolActionBar.RowCount = 2;
				toolActionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				toolActionBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolActionBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolActionBar.SetColumn(toolActionHintLabel, 0);
				toolActionBar.SetRow(toolActionHintLabel, 0);
				toolActionBar.SetColumn(invokeToolButton, 0);
				toolActionBar.SetRow(invokeToolButton, 1);
				toolActionHintLabel.Margin = new Padding(0, 0, 0, 8);
				invokeToolButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				invokeToolButton.Dock = DockStyle.Top;
				invokeToolButton.Margin = new Padding(0);
			}
			else
			{
				toolHeaderLayout.ColumnCount = 2;
				toolHeaderLayout.RowCount = 1;
				toolHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				toolHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				toolHeaderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolHeaderLayout.SetColumn(toolStateLabel, 1);
				toolHeaderLayout.SetRow(toolStateLabel, 0);
				toolStateLabel.Anchor = AnchorStyles.Right;
				toolStateLabel.Margin = new Padding(12, 2, 0, 0);

				toolActionBar.ColumnCount = 2;
				toolActionBar.RowCount = 1;
				toolActionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				toolActionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				toolActionBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				toolActionBar.SetColumn(toolActionHintLabel, 0);
				toolActionBar.SetRow(toolActionHintLabel, 0);
				toolActionBar.SetColumn(invokeToolButton, 1);
				toolActionBar.SetRow(invokeToolButton, 0);
				toolActionHintLabel.Margin = new Padding(0, 11, 12, 0);
				invokeToolButton.Anchor = AnchorStyles.Right;
				invokeToolButton.Dock = DockStyle.None;
				invokeToolButton.Margin = new Padding(12, 0, 0, 0);
			}

			toolHeaderLayout.ResumeLayout();
			toolActionBar.ResumeLayout();
		}

		void ConfigureComposerFooterLayout(bool useStackedLayout)
		{
			composerFooterLayout.SuspendLayout();
			composerFooterLayout.ColumnStyles.Clear();
			composerFooterLayout.RowStyles.Clear();

			if (useStackedLayout)
			{
				composerFooterLayout.ColumnCount = 1;
				composerFooterLayout.RowCount = 6;
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.SetColumn(composerModelLabel, 0);
				composerFooterLayout.SetRow(composerModelLabel, 0);
				composerFooterLayout.SetColumn(modelSelectorComboBox, 0);
				composerFooterLayout.SetRow(modelSelectorComboBox, 1);
				composerFooterLayout.SetColumn(composerStateLabel, 0);
				composerFooterLayout.SetRow(composerStateLabel, 2);
				composerFooterLayout.SetColumn(composerBusyIndicatorLabel, 0);
				composerFooterLayout.SetRow(composerBusyIndicatorLabel, 3);
				composerFooterLayout.SetColumn(modeToggleButton, 0);
				composerFooterLayout.SetRow(modeToggleButton, 4);
				composerFooterLayout.SetColumn(sendButton, 0);
				composerFooterLayout.SetRow(sendButton, 5);
				composerModelLabel.Margin = new Padding(0, 0, 0, 0);
				modelSelectorComboBox.Dock = DockStyle.Fill;
				modelSelectorComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				modelSelectorComboBox.Margin = new Padding(0, 6, 0, 0);
				modelSelectorComboBox.MinimumSize = new Size(0, 0);
				composerStateLabel.Anchor = AnchorStyles.Left;
				composerStateLabel.Margin = new Padding(0, 8, 0, 0);
				composerBusyIndicatorLabel.Anchor = AnchorStyles.Left;
				composerBusyIndicatorLabel.Margin = new Padding(0, 8, 0, 0);
				modeToggleButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				modeToggleButton.Dock = DockStyle.Top;
				modeToggleButton.Margin = new Padding(0, 10, 0, 0);
				sendButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
				sendButton.Dock = DockStyle.Top;
				sendButton.Margin = new Padding(0, 8, 0, 0);
			}
			else
			{
				composerFooterLayout.ColumnCount = 7;
				composerFooterLayout.RowCount = 1;
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
				composerFooterLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
				composerFooterLayout.SetColumn(composerModelLabel, 0);
				composerFooterLayout.SetRow(composerModelLabel, 0);
				composerFooterLayout.SetColumn(modelSelectorComboBox, 1);
				composerFooterLayout.SetRow(modelSelectorComboBox, 0);
				composerFooterLayout.SetColumn(composerStateLabel, 3);
				composerFooterLayout.SetRow(composerStateLabel, 0);
				composerFooterLayout.SetColumn(composerBusyIndicatorLabel, 4);
				composerFooterLayout.SetRow(composerBusyIndicatorLabel, 0);
				composerFooterLayout.SetColumn(modeToggleButton, 5);
				composerFooterLayout.SetRow(modeToggleButton, 0);
				composerFooterLayout.SetColumn(sendButton, 6);
				composerFooterLayout.SetRow(sendButton, 0);
				composerModelLabel.Margin = new Padding(0, 11, 8, 0);
				modelSelectorComboBox.Dock = DockStyle.None;
				modelSelectorComboBox.Anchor = AnchorStyles.Left;
				modelSelectorComboBox.Margin = new Padding(0, 6, 12, 0);
				modelSelectorComboBox.MinimumSize = new Size(168, 0);
				modelSelectorComboBox.Width = 168;
				composerStateLabel.Anchor = AnchorStyles.Right;
				composerStateLabel.Margin = new Padding(12, 2, 0, 0);
				composerBusyIndicatorLabel.Anchor = AnchorStyles.Right;
				composerBusyIndicatorLabel.Margin = new Padding(12, 8, 0, 0);
				modeToggleButton.Anchor = AnchorStyles.Right;
				modeToggleButton.Dock = DockStyle.None;
				modeToggleButton.Margin = new Padding(8, 0, 0, 0);
				sendButton.Anchor = AnchorStyles.Right;
				sendButton.Dock = DockStyle.None;
				sendButton.Margin = new Padding(8, 0, 0, 0);
			}

			composerFooterLayout.ResumeLayout();
		}

		static void ApplyPrimaryButtonState(Button button, bool isEnabled, Color enabledBackColor, Color disabledBackColor)
		{
			button.Enabled = true;
			button.TabStop = isEnabled;
			button.ForeColor = Color.White;
			button.BackColor = isEnabled ? enabledBackColor : disabledBackColor;
			button.FlatAppearance.BorderColor = button.BackColor;
			button.Cursor = isEnabled ? Cursors.Hand : Cursors.Default;
		}

		static string BuildToolDescription(McpToolDescriptor tool)
		{
			var description = string.IsNullOrWhiteSpace(tool.Description) ? "No description provided." : tool.Description;
			return $"{description} Input schema: {tool.InputSchemaJson}";
		}

		static Button CreateActionButton(string text, bool isPrimary = false)
		{
			var button = new Button
			{
				AutoSize = true,
				Text = text,
				MinimumSize = new Size(isPrimary ? 92 : 74, 34),
				BackColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.White,
				ForeColor = isPrimary ? Color.White : Color.FromArgb(63, 55, 48),
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(0, 0, 8, 0),
			};
			button.FlatAppearance.BorderColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.FromArgb(208, 199, 187);
			button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
			return button;
		}

		static Color GetSummaryBackground(McpConnectionState state)
		{
			return state switch
			{
				McpConnectionState.Connected => Color.FromArgb(246, 247, 245),
				McpConnectionState.Connecting => Color.FromArgb(255, 248, 232),
				McpConnectionState.GeneratingResponse => Color.FromArgb(255, 248, 232),
				McpConnectionState.ApprovalRequired => Color.FromArgb(255, 245, 232),
				McpConnectionState.EndpointUnresolved => Color.FromArgb(249, 241, 232),
				McpConnectionState.DiscoveryFailed => Color.FromArgb(255, 243, 230),
				McpConnectionState.ModelResponseFailed => Color.FromArgb(255, 238, 234),
				McpConnectionState.ToolInvocationFailed => Color.FromArgb(255, 238, 234),
				_ => Color.FromArgb(248, 246, 242),
			};
		}

		static Color GetStatusBackground(McpConnectionState state)
		{
			return state switch
			{
				McpConnectionState.Connected => Color.FromArgb(225, 235, 244),
				McpConnectionState.Connecting => Color.FromArgb(255, 242, 212),
				McpConnectionState.GeneratingResponse => Color.FromArgb(255, 242, 212),
				McpConnectionState.ApprovalRequired => Color.FromArgb(255, 237, 214),
				McpConnectionState.EndpointUnresolved => Color.FromArgb(245, 235, 223),
				McpConnectionState.DiscoveryFailed => Color.FromArgb(255, 237, 214),
				McpConnectionState.ModelResponseFailed => Color.FromArgb(255, 228, 225),
				McpConnectionState.ToolInvocationFailed => Color.FromArgb(255, 228, 225),
				_ => Color.FromArgb(236, 232, 226),
			};
		}

		static Color GetStatusForeground(McpConnectionState state)
		{
			return state switch
			{
				McpConnectionState.Connected => Color.FromArgb(52, 78, 112),
				McpConnectionState.Connecting => Color.FromArgb(123, 86, 25),
				McpConnectionState.GeneratingResponse => Color.FromArgb(123, 86, 25),
				McpConnectionState.ApprovalRequired => Color.FromArgb(120, 77, 22),
				McpConnectionState.EndpointUnresolved => Color.FromArgb(120, 84, 34),
				McpConnectionState.DiscoveryFailed => Color.FromArgb(120, 84, 34),
				McpConnectionState.ModelResponseFailed => Color.FromArgb(142, 47, 47),
				McpConnectionState.ToolInvocationFailed => Color.FromArgb(142, 47, 47),
				_ => Color.FromArgb(89, 79, 69),
			};
		}
	}
}

