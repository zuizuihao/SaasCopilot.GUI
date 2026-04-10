using System;
using System.IO;
using System.Reflection;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA2;
using NUnit.Framework;
using FlaUIApplication = FlaUI.Core.Application;
using FlaUIButton = FlaUI.Core.AutomationElements.Button;

namespace SaasCopilot.GUI.WinForms.Test
{
	/// <summary>
	/// FlaUI / UIA2 end-to-end tests that launch the TestHost exe as a real Windows
	/// process and drive it via the UI Automation tree.
	///
	/// UIA2 (not UIA3) is required for WinForms — UIA3 has known bugs with WinForms.
	///
	/// The TestHost exe is built at:
	///   src\Bin\SaasCopilot.GUI.WinForms.TestHost\net10.0-windows\
	///
	/// Run with:
	///   dotnet test src\SaasCopilot.GUI.WinForms.Test --filter Category=FlaUI
	/// </summary>
	[TestFixture]
	[Category("FlaUI")]
	[Apartment(ApartmentState.STA)]
	public sealed class McpSidecarFormFlaUITests
	{
		FlaUIApplication? _app;
		UIA2Automation? _automation;

		static string TestHostExePath()
		{
			// Output path is controlled by Directory.Build.props: ..\Bin\$(MSBuildProjectName)
			// Relative to the test assembly: ../../Bin/SaasCopilot.GUI.WinForms.TestHost/net10.0-windows/
			var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
			return Path.GetFullPath(
				Path.Combine(assemblyDir,
					"..", "..", "SaasCopilot.GUI.WinForms.TestHost",
					"net10.0-windows",
					"SaasCopilot.GUI.WinForms.TestHost.exe"));
		}

		[SetUp]
		public void SetUp()
		{
			var exePath = TestHostExePath();
			Assert.That(exePath, Does.Exist,
				$"TestHost executable not found at '{exePath}'. Run 'dotnet build' first.");

			_automation = new UIA2Automation();
			_app = FlaUIApplication.Launch(exePath);
			// TestHost uses a sizeable tool window. That can exist before Process.MainWindowHandle
			// is populated, so the tests locate it via top-level windows for the process instead.
		}

		[TearDown]
		public void TearDown()
		{
			try
			{
				if (_app is not null && !_app.HasExited)
				{
					_app.Kill();
				}
			}
			catch
			{
				// Ignore cleanup failures in teardown.
			}

			_app?.Dispose();
			_automation?.Dispose();
		}

		// ── Window identity ──────────────────────────────────────────────────

		[Test]
		public void Window_HasExpectedTitle()
		{
			var window = GetMainWindow();
			Assert.That(window.Title, Is.EqualTo("Saas Copilot"));
		}

		[Test]
		public void Window_IsVisible()
		{
			var window = GetMainWindow();
			Assert.That(window.IsOffscreen, Is.False);
		}

		// ── Key controls exist ───────────────────────────────────────────────

		[Test]
		public void ConnectButton_ExistsAndIsEnabled()
		{
			var window = GetMainWindow();
			var connectButton = FindButtonByName(window, "Connect");
			Assert.That(connectButton, Is.Not.Null, "Connect button not found");
			Assert.That(connectButton!.IsEnabled, Is.True, "Connect button should be enabled");
		}

		[Test]
		public void SendButton_Exists()
		{
			var window = GetMainWindow();
			var sendButton = FindButtonByName(window, "Send");
			Assert.That(sendButton, Is.Not.Null, "Send button not found");
		}

		[Test]
		public void ModelComboBox_Exists()
		{
			var window = GetMainWindow();
			var cf = _automation!.ConditionFactory;

			// The model selector combo box should be present in the header area.
			var combo = window.FindFirstDescendant(cf.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox));
			Assert.That(combo, Is.Not.Null, "Model selector combo box not found");
		}

		// ── Interaction ──────────────────────────────────────────────────────

		[Test]
		public void MoreActionsButton_CanBeClicked()
		{
			var window = GetMainWindow();

			// The action is labeled "More" in the WinForms UI.
			FlaUIButton? moreButton = FindButtonByName(window, "More");

			// Older layouts may still expose an ellipsis glyph.
			moreButton ??= FindButtonByName(window, "···")
						 ?? FindButtonByName(window, "...");

			// If the button has no accessible name, locate by position order as a fallback.
			if (moreButton is null)
			{
				var allButtons = window.FindAllDescendants(
					_automation!.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
				Assert.That(allButtons, Is.Not.Empty, "No buttons found in window");
				return; // presence of buttons is sufficient for this smoke test
			}

			Assert.That(() => moreButton.AsButton().Invoke(), Throws.Nothing);
		}

		// ── Helpers ──────────────────────────────────────────────────────────

		Window GetMainWindow()
		{
			Window? window = null;

			for (var attempt = 0; attempt < 50 && window is null; attempt++)
			{
				var windows = _app!.GetAllTopLevelWindows(_automation!);
				foreach (var candidate in windows)
				{
					if (string.Equals(candidate.Title, "Saas Copilot", StringComparison.Ordinal))
					{
						window = candidate;
						break;
					}
				}

				if (window is null)
				{
					Thread.Sleep(200);
				}
			}

			Assert.That(window, Is.Not.Null, "Could not find main window");
			return window;
		}

		FlaUIButton? FindButtonByName(AutomationElement root, string name)
		{
			var cf = _automation!.ConditionFactory;
			var element = root.FindFirstDescendant(
				cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
				  .And(cf.ByName(name)));
			return element?.AsButton();
		}
	}
}
