using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class McpChatTranscriptControl : UserControl
	{
		readonly TranscriptCanvas transcriptCanvas;
		readonly Panel approvalPanel;
		readonly Label approvalTitleLabel;
		readonly Label approvalBodyLabel;
		readonly Button approvalInspectArgumentsButton;
		readonly TextBox approvalPayloadTextBox;
		readonly Font metaFont;
		readonly Font bodyFont;
		readonly Font payloadFont;
		readonly List<ConversationItem> conversationItems = new List<ConversationItem>();
		string? approvalArgumentsContent;

		public McpChatTranscriptControl()
		{
			BackColor = Color.FromArgb(248, 246, 242);
			Dock = DockStyle.Fill;

			metaFont = new Font("Segoe UI", 8.25F, FontStyle.Bold);
			bodyFont = new Font("Segoe UI", 9.25F, FontStyle.Regular);
			payloadFont = new Font("Consolas", 8.5F, FontStyle.Regular);

			transcriptCanvas = new TranscriptCanvas(metaFont, bodyFont, payloadFont)
			{
				Dock = DockStyle.Fill,
				BackColor = BackColor,
			};

			approvalTitleLabel = new Label
			{
				AutoSize = true,
				Font = new Font("Segoe UI", 9F, FontStyle.Bold),
				ForeColor = Color.FromArgb(80, 61, 39),
				Margin = new Padding(0),
			};

			approvalBodyLabel = new Label
			{
				AutoSize = true,
				Font = new Font("Segoe UI", 9F, FontStyle.Regular),
				ForeColor = Color.FromArgb(92, 71, 47),
				MaximumSize = new Size(720, 0),
				Margin = new Padding(0, 8, 0, 0),
			};

			approvalPayloadTextBox = new TextBox
			{
				Dock = DockStyle.Top,
				Multiline = true,
				ReadOnly = true,
				WordWrap = false,
				ScrollBars = ScrollBars.Vertical,
				Height = this.LogicalToDeviceUnits(92),
				Font = payloadFont,
				BorderStyle = BorderStyle.FixedSingle,
				BackColor = Color.FromArgb(252, 250, 247),
				Margin = new Padding(0, 10, 0, 0),
				Visible = false,
			};

			var approvalButtons = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				WrapContents = true,
				BackColor = Color.Transparent,
				Margin = new Padding(0, 12, 0, 0),
			};

			approvalInspectArgumentsButton = CreateApprovalButton("Inspect arguments", isPrimary: false);
			approvalInspectArgumentsButton.MinimumSize = new Size(128, 34);
			approvalInspectArgumentsButton.Click += (_, _) => ShowApprovalArguments();
			approvalButtons.Controls.Add(approvalInspectArgumentsButton);

			var approveOnceButton = CreateApprovalButton("Approve Once", isPrimary: true);
			approveOnceButton.Click += (_, _) => ApproveOnceClicked?.Invoke(this, EventArgs.Empty);
			approvalButtons.Controls.Add(approveOnceButton);

			var approveAlwaysButton = CreateApprovalButton("Always Allow", isPrimary: false);
			approveAlwaysButton.Click += (_, _) => ApproveAlwaysClicked?.Invoke(this, EventArgs.Empty);
			approvalButtons.Controls.Add(approveAlwaysButton);

			var declineButton = CreateApprovalButton("Decline", isPrimary: false);
			declineButton.Click += (_, _) => DeclineClicked?.Invoke(this, EventArgs.Empty);
			approvalButtons.Controls.Add(declineButton);

			var approvalLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				AutoSize = true,
				BackColor = Color.FromArgb(255, 246, 234),
				Padding = new Padding(14),
			};
			approvalLayout.Controls.Add(approvalTitleLabel, 0, 0);
			approvalLayout.Controls.Add(approvalBodyLabel, 0, 1);
			approvalLayout.Controls.Add(approvalPayloadTextBox, 0, 2);
			approvalLayout.Controls.Add(approvalButtons, 0, 3);

			approvalPanel = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 0,
				Visible = false,
				BackColor = Color.FromArgb(255, 246, 234),
				BorderStyle = BorderStyle.FixedSingle,
			};
			approvalPanel.Controls.Add(approvalLayout);

			Controls.Add(transcriptCanvas);
			Controls.Add(approvalPanel);
		}

		public event EventHandler? ApproveOnceClicked;

		public event EventHandler? ApproveAlwaysClicked;

		public event EventHandler? DeclineClicked;

		public void SetConversation(IReadOnlyList<McpTranscriptEntry> entries, McpApprovalRequest? approvalRequest)
		{
			ArgumentNullException.ThrowIfNull(entries);

			var shouldScrollToBottom = transcriptCanvas.IsNearBottom();
			conversationItems.Clear();

			if (entries.Count == 0)
			{
				conversationItems.Add(new ConversationItem(
					ConversationItemKind.Status,
					"status",
					"Ready",
					"Ask a question to start. Prompts, assistant responses, tool calls, approvals, and results will appear here in one thread.",
					null,
					null,
					string.Empty,
					false,
					false,
					false));
			}

			foreach (var entry in entries)
			{
				conversationItems.Add(MapConversationItem(entry));
			}

			UpdateApprovalPanel(approvalRequest);
			transcriptCanvas.SetItems(conversationItems, shouldScrollToBottom);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				approvalPayloadTextBox.Dispose();
				approvalBodyLabel.Dispose();
				approvalInspectArgumentsButton.Dispose();
				approvalTitleLabel.Dispose();
				approvalPanel.Dispose();
				transcriptCanvas.Dispose();
				metaFont.Dispose();
				bodyFont.Dispose();
				payloadFont.Dispose();
			}

			base.Dispose(disposing);
		}

		void UpdateApprovalPanel(McpApprovalRequest? approvalRequest)
		{
			if (approvalRequest is null)
			{
				approvalPanel.Visible = false;
				approvalPanel.Height = 0;
				approvalArgumentsContent = null;
				approvalPayloadTextBox.Clear();
				return;
			}

			approvalTitleLabel.Text = $"Approval needed  {DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)}";
			approvalBodyLabel.Text = $"'{approvalRequest.Tool.Name}' wants to run on {approvalRequest.Endpoint.Authority}. Review the arguments, then approve once, trust it, or decline.";
			approvalArgumentsContent = McpJsonFormatting.NormalizeText(approvalRequest.ArgumentsJson, out _);
			approvalPayloadTextBox.Text = approvalArgumentsContent;
			approvalInspectArgumentsButton.Visible = !string.IsNullOrWhiteSpace(approvalArgumentsContent);
			approvalPanel.Visible = true;
			approvalPanel.Height = this.LogicalToDeviceUnits(128);
		}

		void ShowApprovalArguments()
		{
			if (string.IsNullOrWhiteSpace(approvalArgumentsContent))
			{
				return;
			}

			using var dialog = new Form
			{
				Text = approvalTitleLabel.Text,
				StartPosition = FormStartPosition.CenterParent,
				Size = new Size(this.LogicalToDeviceUnits(900), this.LogicalToDeviceUnits(620)),
				MinimumSize = new Size(this.LogicalToDeviceUnits(560), this.LogicalToDeviceUnits(420)),
				ShowInTaskbar = false,
			};

			var contentTextBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				WordWrap = false,
				ScrollBars = ScrollBars.Both,
				Font = payloadFont,
				Text = approvalArgumentsContent,
				BackColor = Color.White,
			};

			dialog.Controls.Add(contentTextBox);
			dialog.ShowDialog(FindForm());
		}

		static ConversationItem MapConversationItem(McpTranscriptEntry entry)
		{
			var timestamp = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
			var title = string.IsNullOrWhiteSpace(entry.Title) ? GetDefaultTitle(entry.Kind) : entry.Title;
			var meta = string.IsNullOrWhiteSpace(title) ? timestamp : $"{title}  {timestamp}";
			var body = NormalizeDisplayText(entry.Message, out var bodyUsesPayloadFont);
			var payload = NormalizeOptionalDisplayText(entry.Payload, out var payloadUsesPayloadFont);
			var forceCollapsedDetail = ShouldAlwaysCollapseDetail(entry.Kind);
			var detailPreview = BuildContentPreview(payload);
			var detailContent = (forceCollapsedDetail && payload is not null) || ShouldCompactContent(payload) ? payload : null;
			var detailActionLabel = BuildDetailActionLabel(entry.Kind, detailContent);
			var allowDetailExport = ShouldAllowDetailExport(entry.Kind, detailContent);

			if (detailContent is null && bodyUsesPayloadFont && (forceCollapsedDetail || ShouldCompactContent(body)))
			{
				detailPreview = BuildContentPreview(body);
				detailContent = body;
				payloadUsesPayloadFont = true;
				bodyUsesPayloadFont = false;
				body = BuildCompactBody(entry.Kind);
			}
			else if (detailContent is not null && (string.IsNullOrWhiteSpace(body) || bodyUsesPayloadFont || string.Equals(body, payload, StringComparison.Ordinal)))
			{
				bodyUsesPayloadFont = false;
				body = BuildCompactBody(entry.Kind);
			}

			if (forceCollapsedDetail)
			{
				detailPreview = null;
			}

			return new ConversationItem(
				MapKind(entry.Kind),
				entry.Kind,
				meta,
				body,
				detailPreview,
				detailContent,
				detailActionLabel,
				allowDetailExport,
				bodyUsesPayloadFont,
				payloadUsesPayloadFont);
		}

		static ConversationItemKind MapKind(string kind)
		{
			return kind switch
			{
				"user" => ConversationItemKind.User,
				"assistant" => ConversationItemKind.Assistant,
				"tool" => ConversationItemKind.Tool,
				"result" => ConversationItemKind.Result,
				"error" => ConversationItemKind.Error,
				"system" => ConversationItemKind.Status,
				"working" => ConversationItemKind.Activity,
				"considering" => ConversationItemKind.Activity,
				"processing" => ConversationItemKind.Activity,
				"approval" => ConversationItemKind.Tool,
				"config" => ConversationItemKind.Activity,
				"model" => ConversationItemKind.Activity,
				_ => ConversationItemKind.Activity,
			};
		}

		static string GetDefaultTitle(string kind)
		{
			return kind switch
			{
				"user" => "You",
				"assistant" => "Assistant",
				"tool" => "Tool call",
				"result" => "Tool result",
				"error" => "Error",
				"working" => "Working",
				"considering" => "Thinking",
				"processing" => "Processing",
				"config" => "Configuration",
				"model" => "Model",
				_ => "Activity",
			};
		}

		static bool ShouldAlwaysCollapseDetail(string kind)
		{
			return kind switch
			{
				"tool" => true,
				"result" => true,
				"approval" => true,
				_ => false,
			};
		}

		static string BuildDetailActionLabel(string kind, string? detailContent)
		{
			if (string.IsNullOrWhiteSpace(detailContent))
			{
				return string.Empty;
			}

			return kind switch
			{
				"approval" => "Inspect arguments",
				"tool" => "Inspect arguments",
				_ => "View full",
			};
		}

		static bool ShouldAllowDetailExport(string kind, string? detailContent)
		{
			if (string.IsNullOrWhiteSpace(detailContent))
			{
				return false;
			}

			return kind switch
			{
				"approval" => false,
				"tool" => false,
				_ => true,
			};
		}

		static string? NormalizeOptionalDisplayText(string? text, out bool usesPayloadFont)
		{
			usesPayloadFont = false;
			return string.IsNullOrWhiteSpace(text) ? null : NormalizeDisplayText(text, out usesPayloadFont);
		}

		static string? BuildContentPreview(string? content)
		{
			if (string.IsNullOrWhiteSpace(content))
			{
				return null;
			}

			if (!ShouldCompactContent(content))
			{
				return content;
			}

			var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
			var lines = normalized.Split('\n');
			var previewLines = new List<string>();
			var characterCount = 0;
			for (var index = 0; index < lines.Length && index < 8; index++)
			{
				var line = lines[index];
				if (characterCount + line.Length > 900)
				{
					break;
				}

				previewLines.Add(line);
				characterCount += line.Length;
			}

			if (previewLines.Count == 0)
			{
				previewLines.Add(normalized.Substring(0, Math.Min(normalized.Length, 180)));
			}

			var preview = string.Join(Environment.NewLine, previewLines);
			return preview.EndsWith("...", StringComparison.Ordinal) ? preview : preview + Environment.NewLine + "...";
		}

		static bool ShouldCompactContent(string? content)
		{
			if (string.IsNullOrWhiteSpace(content))
			{
				return false;
			}

			var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
			if (normalized.Length > 900)
			{
				return true;
			}

			var lines = normalized.Split('\n');
			if (lines.Length > 10)
			{
				return true;
			}

			for (var index = 0; index < lines.Length; index++)
			{
				if (lines[index].Length > 140)
				{
					return true;
				}
			}

			return false;
		}

		static string BuildCompactBody(string kind)
		{
			return kind switch
			{
				"result" => "Large MCP result preview. Use View full or Open .txt to inspect the complete content.",
				"tool" => "Large MCP payload preview. Use View full or Open .txt to inspect the complete content.",
				"approval" => "Large approval payload preview. Use View full or Open .txt to inspect the complete content.",
				_ => "Large content preview. Use View full or Open .txt to inspect the complete content.",
			};
		}

		static string NormalizeDisplayText(string text, out bool usesPayloadFont)
		{
			return McpJsonFormatting.NormalizeText(text, out usesPayloadFont);
		}

		static Button CreateApprovalButton(string text, bool isPrimary)
		{
			var button = new Button
			{
				AutoSize = true,
				Text = text,
				MinimumSize = new Size(110, 34),
				BackColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.White,
				ForeColor = isPrimary ? Color.White : Color.FromArgb(63, 55, 48),
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(0, 0, 8, 0),
			};
			button.FlatAppearance.BorderColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.FromArgb(208, 199, 187);
			button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
			return button;
		}

		enum ConversationItemKind
		{
			User,
			Assistant,
			Tool,
			Result,
			Error,
			Status,
			Activity,
		}

		enum ConversationAlignment
		{
			Left,
			Center,
			Right,
		}

		sealed class ConversationItem
		{
			public ConversationItem(ConversationItemKind kind, string sourceKind, string meta, string body, string? detailPreview, string? fullContent, string detailActionLabel, bool allowDetailExport, bool bodyUsesPayloadFont, bool detailUsesPayloadFont)
			{
				Kind = kind;
				SourceKind = sourceKind;
				Meta = meta;
				Body = body;
				DetailPreview = detailPreview;
				FullContent = fullContent;
				DetailActionLabel = detailActionLabel;
				AllowDetailExport = allowDetailExport;
				BodyUsesPayloadFont = bodyUsesPayloadFont;
				DetailUsesPayloadFont = detailUsesPayloadFont;
			}

			public ConversationItemKind Kind { get; }

			public string SourceKind { get; }

			public ConversationAlignment Alignment => Kind switch
			{
				ConversationItemKind.User => ConversationAlignment.Right,
				ConversationItemKind.Status => ConversationAlignment.Center,
				_ => ConversationAlignment.Left,
			};

			public string Meta { get; }

			public string Body { get; }

			public string? DetailPreview { get; }

			public string? FullContent { get; }

			public string DetailActionLabel { get; }

			public bool AllowDetailExport { get; }

			public bool BodyUsesPayloadFont { get; }

			public bool DetailUsesPayloadFont { get; }

			public bool HasCompactDetail => !string.IsNullOrWhiteSpace(FullContent);

			public string? ExportedContentPath { get; set; }
		}

		sealed class ConversationLayoutItem
		{
			public ConversationLayoutItem(ConversationItem item, Rectangle bubbleBounds, Rectangle metaBounds, Rectangle bodyBounds, Rectangle? detailBounds, Rectangle? viewFullBounds, Rectangle? exportBounds)
			{
				Item = item;
				BubbleBounds = bubbleBounds;
				MetaBounds = metaBounds;
				BodyBounds = bodyBounds;
				DetailBounds = detailBounds;
				ViewFullBounds = viewFullBounds;
				ExportBounds = exportBounds;
			}

			public ConversationItem Item { get; }

			public Rectangle BubbleBounds { get; }

			public Rectangle MetaBounds { get; }

			public Rectangle BodyBounds { get; }

			public Rectangle? DetailBounds { get; }

			public Rectangle? ViewFullBounds { get; }

			public Rectangle? ExportBounds { get; }

			public int Bottom => BubbleBounds.Bottom;
		}

		sealed class TranscriptCanvas : ScrollableControl
		{
			readonly Font metaFont;
			readonly Font bodyFont;
			readonly Font payloadFont;
			readonly List<ConversationItem> items = new List<ConversationItem>();
			readonly List<ConversationLayoutItem> layoutItems = new List<ConversationLayoutItem>();

			bool layoutDirty = true;
			int cachedWidth = -1;

			public TranscriptCanvas(Font metaFont, Font bodyFont, Font payloadFont)
			{
				this.metaFont = metaFont;
				this.bodyFont = bodyFont;
				this.payloadFont = payloadFont;

				AutoScroll = true;
				DoubleBuffered = true;
				ResizeRedraw = true;
				SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
			}

			public bool IsNearBottom()
			{
				EnsureLayout();

				if (layoutItems.Count == 0)
				{
					return true;
				}

				var viewportBottom = -AutoScrollPosition.Y + ClientSize.Height;
				return layoutItems[layoutItems.Count - 1].Bottom - viewportBottom <= LogicalToDeviceUnits(36);
			}

			public void SetItems(IReadOnlyList<ConversationItem> sourceItems, bool scrollToBottom)
			{
				items.Clear();
				for (var index = 0; index < sourceItems.Count; index++)
				{
					items.Add(sourceItems[index]);
				}

				layoutDirty = true;
				EnsureLayout();

				if (scrollToBottom)
				{
					ScrollToBottom();
				}

				Invalidate();
			}

			protected override void OnResize(EventArgs e)
			{
				var shouldStickToBottom = IsNearBottom();
				base.OnResize(e);
				layoutDirty = true;
				EnsureLayout();
				if (shouldStickToBottom)
				{
					ScrollToBottom();
				}
				Invalidate();
			}

			protected override void OnScroll(ScrollEventArgs se)
			{
				base.OnScroll(se);
				Invalidate();
			}

			protected override void OnMouseDown(MouseEventArgs e)
			{
				Focus();
				base.OnMouseDown(e);
			}

			protected override void OnMouseClick(MouseEventArgs e)
			{
				base.OnMouseClick(e);
				EnsureLayout();

				var contentPoint = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
				for (var index = 0; index < layoutItems.Count; index++)
				{
					var layoutItem = layoutItems[index];
					if (layoutItem.ViewFullBounds is Rectangle viewFullBounds && viewFullBounds.Contains(contentPoint))
					{
						ShowFullContent(layoutItem.Item);
						return;
					}

					if (layoutItem.ExportBounds is Rectangle exportBounds && exportBounds.Contains(contentPoint))
					{
						OpenContentCopy(layoutItem.Item);
						return;
					}
				}
			}

			protected override void OnMouseMove(MouseEventArgs e)
			{
				base.OnMouseMove(e);
				EnsureLayout();

				var contentPoint = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
				for (var index = 0; index < layoutItems.Count; index++)
				{
					var layoutItem = layoutItems[index];
					if ((layoutItem.ViewFullBounds is Rectangle viewFullBounds && viewFullBounds.Contains(contentPoint))
						|| (layoutItem.ExportBounds is Rectangle exportBounds && exportBounds.Contains(contentPoint)))
					{
						Cursor = Cursors.Hand;
						return;
					}
				}

				Cursor = Cursors.Default;
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);
				EnsureLayout();

				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

				var viewportTop = -AutoScrollPosition.Y;
				var viewportBottom = viewportTop + ClientSize.Height;
				var scrollY = AutoScrollPosition.Y;

				for (var index = 0; index < layoutItems.Count; index++)
				{
					var layoutItem = layoutItems[index];
					if (layoutItem.Bottom < viewportTop)
					{
						continue;
					}

					if (layoutItem.BubbleBounds.Top > viewportBottom)
					{
						break;
					}

					DrawConversationItem(e.Graphics, layoutItem, scrollY);
				}
			}

			void EnsureLayout()
			{
				var availableWidth = Math.Max(LogicalToDeviceUnits(160), ClientSize.Width - LogicalToDeviceUnits(28));
				if (!layoutDirty && cachedWidth == availableWidth)
				{
					return;
				}

				cachedWidth = availableWidth;
				layoutDirty = false;
				layoutItems.Clear();

				var currentY = LogicalToDeviceUnits(10);
				for (var index = 0; index < items.Count; index++)
				{
					var layoutItem = BuildLayoutItem(items[index], availableWidth, currentY);
					layoutItems.Add(layoutItem);
					currentY = layoutItem.Bottom + LogicalToDeviceUnits(12);
				}

				AutoScrollMinSize = new Size(0, Math.Max(ClientSize.Height, currentY + LogicalToDeviceUnits(8)));
			}

			ConversationLayoutItem BuildLayoutItem(ConversationItem item, int availableWidth, int y)
			{
				var bubbleWidth = GetBubbleWidth(availableWidth, item.Kind);
				var bubbleX = item.Alignment switch
				{
					ConversationAlignment.Right => LogicalToDeviceUnits(10) + Math.Max(0, availableWidth - bubbleWidth),
					ConversationAlignment.Center => LogicalToDeviceUnits(10) + Math.Max(0, (availableWidth - bubbleWidth) / 2),
					_ => LogicalToDeviceUnits(10),
				};

				var contentWidth = Math.Max(LogicalToDeviceUnits(90), bubbleWidth - LogicalToDeviceUnits(30));
				var metaHeight = MeasureTextHeight(item.Meta, metaFont, contentWidth);
				var bodyFontForItem = item.BodyUsesPayloadFont ? payloadFont : bodyFont;
				var bodyHeight = MeasureTextHeight(item.Body, bodyFontForItem, contentWidth);
				var detailFontForItem = item.DetailUsesPayloadFont ? payloadFont : bodyFont;
				var detailHeight = string.IsNullOrWhiteSpace(item.DetailPreview)
					? 0
					: MeasureTextHeight(item.DetailPreview, detailFontForItem, contentWidth - LogicalToDeviceUnits(16)) + LogicalToDeviceUnits(22);
				var actionHeight = item.HasCompactDetail ? LogicalToDeviceUnits(30) : 0;
				var bubbleHeight = metaHeight + bodyHeight + detailHeight + actionHeight + LogicalToDeviceUnits(28);
				var bubbleBounds = new Rectangle(bubbleX, y, bubbleWidth, Math.Max(LogicalToDeviceUnits(48), bubbleHeight));

				var contentBounds = Rectangle.Inflate(bubbleBounds, -LogicalToDeviceUnits(15), -LogicalToDeviceUnits(12));
				var metaBounds = new Rectangle(contentBounds.Left, contentBounds.Top, contentBounds.Width, metaHeight);
				var bodyBounds = new Rectangle(contentBounds.Left, metaBounds.Bottom + LogicalToDeviceUnits(4), contentBounds.Width, bodyHeight);
				Rectangle? detailBounds = null;
				Rectangle? viewFullBounds = null;
				Rectangle? exportBounds = null;
				if (!string.IsNullOrWhiteSpace(item.DetailPreview))
				{
					detailBounds = new Rectangle(
						contentBounds.Left,
						bodyBounds.Bottom + LogicalToDeviceUnits(8),
						contentBounds.Width,
						Math.Max(LogicalToDeviceUnits(44), detailHeight));
				}

				if (item.HasCompactDetail)
				{
					var actionY = (detailBounds?.Bottom ?? bodyBounds.Bottom) + LogicalToDeviceUnits(8);
					var viewWidth = MeasureActionWidth(item.DetailActionLabel);
					viewFullBounds = new Rectangle(contentBounds.Left, actionY, viewWidth, LogicalToDeviceUnits(24));
					if (item.AllowDetailExport)
					{
						var exportX = viewFullBounds.Value.Right + LogicalToDeviceUnits(8);
						exportBounds = new Rectangle(exportX, actionY, MeasureActionWidth("Open .txt"), LogicalToDeviceUnits(24));
					}
				}

				return new ConversationLayoutItem(item, bubbleBounds, metaBounds, bodyBounds, detailBounds, viewFullBounds, exportBounds);
			}

			void DrawConversationItem(Graphics graphics, ConversationLayoutItem layoutItem, int scrollY)
			{
				var bubbleBounds = Offset(layoutItem.BubbleBounds, scrollY);
				var metaBounds = Offset(layoutItem.MetaBounds, scrollY);
				var bodyBounds = Offset(layoutItem.BodyBounds, scrollY);
				var bodyFontForItem = layoutItem.Item.BodyUsesPayloadFont ? payloadFont : bodyFont;

				using var backgroundBrush = new SolidBrush(GetBackgroundColor(layoutItem.Item.Kind));
				using var borderPen = new Pen(GetBorderColor(layoutItem.Item.Kind));
				FillRoundedRectangle(graphics, backgroundBrush, bubbleBounds, LogicalToDeviceUnits(12));
				DrawRoundedRectangle(graphics, borderPen, bubbleBounds, LogicalToDeviceUnits(12));

				TextRenderer.DrawText(graphics, layoutItem.Item.Meta, metaFont, metaBounds, GetMetaColor(layoutItem.Item.Kind), TextMeasureFlags);
				TextRenderer.DrawText(graphics, layoutItem.Item.Body, bodyFontForItem, bodyBounds, GetBodyColor(layoutItem.Item.Kind), TextMeasureFlags);

				if (layoutItem.DetailBounds is Rectangle detailBounds)
				{
					var visibleDetailBounds = Offset(detailBounds, scrollY);
					using var payloadBrush = new SolidBrush(GetPayloadBackColor(layoutItem.Item.Kind));
					FillRoundedRectangle(graphics, payloadBrush, visibleDetailBounds, LogicalToDeviceUnits(8));
					DrawRoundedRectangle(graphics, borderPen, visibleDetailBounds, LogicalToDeviceUnits(8));
					var detailTextBounds = Rectangle.Inflate(visibleDetailBounds, -LogicalToDeviceUnits(8), -LogicalToDeviceUnits(8));
					var detailFontForItem = layoutItem.Item.DetailUsesPayloadFont ? payloadFont : bodyFont;
					TextRenderer.DrawText(graphics, layoutItem.Item.DetailPreview, detailFontForItem, detailTextBounds, Color.FromArgb(73, 66, 59), TextMeasureFlags);
				}

				if (layoutItem.ViewFullBounds is Rectangle viewFullBounds)
				{
					DrawActionChip(graphics, Offset(viewFullBounds, scrollY), layoutItem.Item.DetailActionLabel);
				}

				if (layoutItem.ExportBounds is Rectangle exportBounds)
				{
					DrawActionChip(graphics, Offset(exportBounds, scrollY), "Open .txt");
				}
			}

			void ScrollToBottom()
			{
				EnsureLayout();
				if (layoutItems.Count == 0)
				{
					return;
				}

				var totalHeight = AutoScrollMinSize.Height;
				var scrollY = Math.Max(0, totalHeight - ClientSize.Height);
				AutoScrollPosition = new Point(0, scrollY);
			}

			static Rectangle Offset(Rectangle bounds, int offsetY)
			{
				return new Rectangle(bounds.X, bounds.Y + offsetY, bounds.Width, bounds.Height);
			}

			int GetBubbleWidth(int availableWidth, ConversationItemKind kind)
			{
				var ratio = kind switch
				{
					ConversationItemKind.User => 0.64F,
					ConversationItemKind.Status => 0.58F,
					ConversationItemKind.Assistant => 0.78F,
					ConversationItemKind.Result => 0.78F,
					_ => 0.72F,
				};
				var maxWidth = (int)(availableWidth * ratio);
				var minimumWidth = Math.Min(availableWidth, LogicalToDeviceUnits(220));
				return Math.Max(minimumWidth, Math.Min(availableWidth, maxWidth));
			}

			void ShowFullContent(ConversationItem item)
			{
				if (string.IsNullOrWhiteSpace(item.FullContent))
				{
					return;
				}

				using var dialog = new Form
				{
					Text = item.Meta,
					StartPosition = FormStartPosition.CenterParent,
					Size = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(620)),
					MinimumSize = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(420)),
					ShowInTaskbar = false,
				};

				var contentTextBox = new TextBox
				{
					Dock = DockStyle.Fill,
					Multiline = true,
					ReadOnly = true,
					WordWrap = !item.DetailUsesPayloadFont,
					ScrollBars = ScrollBars.Both,
					BorderStyle = BorderStyle.FixedSingle,
					BackColor = Color.FromArgb(252, 250, 247),
					Font = item.DetailUsesPayloadFont ? payloadFont : bodyFont,
					Text = item.FullContent,
				};

				var actionPanel = new FlowLayoutPanel
				{
					Dock = DockStyle.Bottom,
					AutoSize = true,
					WrapContents = true,
					FlowDirection = FlowDirection.RightToLeft,
					Padding = new Padding(12),
				};

				var closeButton = CreateActionButton("Close");
				closeButton.Click += (_, _) => dialog.Close();
				actionPanel.Controls.Add(closeButton);

				var openCopyButton = CreateActionButton("Open .txt", isPrimary: true);
				openCopyButton.Click += (_, _) => OpenContentCopy(item);
				actionPanel.Controls.Add(openCopyButton);

				dialog.Controls.Add(contentTextBox);
				dialog.Controls.Add(actionPanel);
				dialog.ShowDialog(FindForm());
			}

			static void OpenContentCopy(ConversationItem item)
			{
				if (string.IsNullOrWhiteSpace(item.FullContent))
				{
					return;
				}

				item.ExportedContentPath ??= ExportContentCopy(item.FullContent);
				using var process = Process.Start(new ProcessStartInfo(item.ExportedContentPath)
				{
					UseShellExecute = true,
				});
			}

			static string ExportContentCopy(string content)
			{
				var directory = Path.Combine(Path.GetTempPath(), "mcp");
				Directory.CreateDirectory(directory);
				var filePath = Path.Combine(directory, $"content-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.txt");
				File.WriteAllText(filePath, content);
				return filePath;
			}

			int MeasureActionWidth(string text)
			{
				return TextRenderer.MeasureText(text, metaFont, Size.Empty, ActionTextFlags).Width + LogicalToDeviceUnits(20);
			}

			void DrawActionChip(Graphics graphics, Rectangle bounds, string text)
			{
				using var actionBrush = new SolidBrush(Color.FromArgb(252, 250, 247));
				using var actionBorderPen = new Pen(Color.FromArgb(197, 188, 177));
				FillRoundedRectangle(graphics, actionBrush, bounds, LogicalToDeviceUnits(8));
				DrawRoundedRectangle(graphics, actionBorderPen, bounds, LogicalToDeviceUnits(8));
				TextRenderer.DrawText(graphics, text, metaFont, bounds, Color.FromArgb(72, 87, 109), ActionTextFlags);
			}

			static int MeasureTextHeight(string text, Font font, int width)
			{
				return Math.Max(font.Height + 2, TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextMeasureFlags).Height);
			}

			static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, int radius)
			{
				using var path = CreateRoundedPath(bounds, radius);
				graphics.FillPath(brush, path);
			}

			static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle bounds, int radius)
			{
				using var path = CreateRoundedPath(bounds, radius);
				graphics.DrawPath(pen, path);
			}

			static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
			{
				var diameter = radius * 2;
				var path = new GraphicsPath();
				path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
				path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
				path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
				path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
				path.CloseFigure();
				return path;
			}

			static Color GetBackgroundColor(ConversationItemKind kind)
			{
				return kind switch
				{
					ConversationItemKind.User => Color.FromArgb(74, 98, 126),
					ConversationItemKind.Assistant => Color.FromArgb(255, 255, 255),
					ConversationItemKind.Tool => Color.FromArgb(244, 247, 251),
					ConversationItemKind.Result => Color.FromArgb(241, 247, 242),
					ConversationItemKind.Error => Color.FromArgb(255, 242, 238),
					ConversationItemKind.Status => Color.FromArgb(244, 241, 236),
					_ => Color.FromArgb(246, 244, 240),
				};
			}

			static Color GetBorderColor(ConversationItemKind kind)
			{
				return kind switch
				{
					ConversationItemKind.User => Color.FromArgb(74, 98, 126),
					ConversationItemKind.Assistant => Color.FromArgb(220, 214, 205),
					ConversationItemKind.Tool => Color.FromArgb(200, 214, 228),
					ConversationItemKind.Result => Color.FromArgb(190, 209, 191),
					ConversationItemKind.Error => Color.FromArgb(226, 178, 166),
					ConversationItemKind.Status => Color.FromArgb(214, 204, 191),
					_ => Color.FromArgb(216, 207, 196),
				};
			}

			static Color GetMetaColor(ConversationItemKind kind)
			{
				return kind == ConversationItemKind.User ? Color.FromArgb(230, 237, 246) : Color.FromArgb(108, 95, 82);
			}

			static Color GetBodyColor(ConversationItemKind kind)
			{
				return kind == ConversationItemKind.User ? Color.White : Color.FromArgb(65, 56, 48);
			}

			static Color GetPayloadBackColor(ConversationItemKind kind)
			{
				return kind == ConversationItemKind.User ? Color.FromArgb(62, 85, 112) : Color.FromArgb(252, 250, 247);
			}

			const TextFormatFlags TextMeasureFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
			const TextFormatFlags ActionTextFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
		}

		static Button CreateActionButton(string text, bool isPrimary = false)
		{
			var button = new Button
			{
				AutoSize = true,
				Text = text,
				MinimumSize = new Size(96, 34),
				BackColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.White,
				ForeColor = isPrimary ? Color.White : Color.FromArgb(63, 55, 48),
				FlatStyle = FlatStyle.Flat,
				Margin = new Padding(8, 0, 0, 0),
			};
			button.FlatAppearance.BorderColor = isPrimary ? Color.FromArgb(74, 98, 126) : Color.FromArgb(208, 199, 187);
			button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
			return button;
		}
	}
}
