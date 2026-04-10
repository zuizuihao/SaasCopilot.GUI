using System.Runtime.Versioning;
using NUnit.Framework;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	[TestFixture]
	[SupportedOSPlatform("windows10.0.17763.0")]
	public class McpDirectToolIntentParserTests
	{
		[Test]
		public void TryBuildIntentWhenFileReadMentionContainsWindowsPathUsesExactPath()
		{
			var tool = new McpToolDescriptor
			{
				Name = "file_read",
				Title = "Read File",
				InputSchemaJson = "{}",
			};

			var success = McpDirectToolIntentParser.TryBuildIntent(
				"#file_read C:\\git\\github\\WiseTechGlobal\\SaasCopilot.Client\\App\\SaasCopilot.Copilot.GUI\\Features\\Mcp\\Application\\McpSidecarController.cs",
				tool,
				out var toolIntent);

			Assert.That(success, Is.True);
			Assert.That(toolIntent, Is.Not.Null);
			Assert.That(toolIntent!.ToolName, Is.EqualTo("file_read"));
			Assert.That(toolIntent.ArgumentsJson, Is.EqualTo("{\"path\":\"C:\\\\git\\\\github\\\\WiseTechGlobal\\\\SaasCopilot.Client\\\\App\\\\SaasCopilot.Copilot.GUI\\\\Features\\\\Mcp\\\\Application\\\\McpSidecarController.cs\"}"));
		}

		[Test]
		public void TryBuildIntentWhenJsonArgumentsProvidedPreservesJsonObject()
		{
			var tool = new McpToolDescriptor
			{
				Name = "file_read",
				Title = "Read File",
				InputSchemaJson = "{}",
			};

			var success = McpDirectToolIntentParser.TryBuildIntent(
				"#file_read {\"path\":\"C:\\\\tmp\\\\test.txt\"}",
				tool,
				out var toolIntent);

			Assert.That(success, Is.True);
			Assert.That(toolIntent, Is.Not.Null);
			Assert.That(toolIntent!.ArgumentsJson, Is.EqualTo("{\"path\":\"C:\\\\tmp\\\\test.txt\"}"));
		}

		[Test]
		public void TryBuildIntentWhenRunCommandMentionContainsCommandUsesExactCommand()
		{
			var tool = new McpToolDescriptor
			{
				Name = "run_command",
				Title = "Run Command",
				InputSchemaJson = "{}",
			};

			var success = McpDirectToolIntentParser.TryBuildIntent(
				"#run_command git status --short",
				tool,
				out var toolIntent);

			Assert.That(success, Is.True);
			Assert.That(toolIntent, Is.Not.Null);
			Assert.That(toolIntent!.ArgumentsJson, Is.EqualTo("{\"command\":\"git status --short\"}"));
		}

		[Test]
		public void TryBuildIntentWhenSearchWebMentionContainsAbsoluteUrlUsesExactUrl()
		{
			var tool = new McpToolDescriptor
			{
				Name = "search_web",
				Title = "Search Web",
				InputSchemaJson = "{}",
			};

			var success = McpDirectToolIntentParser.TryBuildIntent(
				"#search_web https://example.com/docs?q=test",
				tool,
				out var toolIntent);

			Assert.That(success, Is.True);
			Assert.That(toolIntent, Is.Not.Null);
			Assert.That(toolIntent!.ArgumentsJson, Is.EqualTo("{\"url\":\"https://example.com/docs?q=test\"}"));
		}

		[Test]
		public void TryBuildIntentWhenToolNeedsAmbiguousArgumentsReturnsFalse()
		{
			var tool = new McpToolDescriptor
			{
				Name = "file_write",
				Title = "Write File",
				InputSchemaJson = "{}",
			};

			var success = McpDirectToolIntentParser.TryBuildIntent(
				"#file_write C:\\tmp\\test.txt",
				tool,
				out var toolIntent);

			Assert.That(success, Is.False);
			Assert.That(toolIntent, Is.Null);
		}

		[Test]
		public void TryParseWhenTaggedToolRequestProvidedReturnsIntent()
		{
			var success = McpToolRequestTextParser.TryParse(
				"[TOOL_REQUEST]\n{\"name\":\"file_read\",\"arguments\":\"{\\\"path\\\":\\\"C:\\\\\\\\git\\\\\\\\repo\\\\\\\\file.cs\\\"}\"}\n[END_TOOL_REQUEST]",
				out var toolIntent);

			Assert.That(success, Is.True);
			Assert.That(toolIntent, Is.Not.Null);
			Assert.That(toolIntent!.ToolName, Is.EqualTo("file_read"));
			Assert.That(toolIntent.ArgumentsJson, Is.EqualTo("{\"path\":\"C:\\\\git\\\\repo\\\\file.cs\"}"));
		}
	}
}