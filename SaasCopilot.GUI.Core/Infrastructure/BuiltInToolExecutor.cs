using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class BuiltInToolExecutor : IBuiltInToolExecutor
	{
		const int MaxOutputLength = 8000;

		readonly IHttpClientFactory httpClientFactory;

		public BuiltInToolExecutor(IHttpClientFactory httpClientFactory)
		{
			ArgumentNullException.ThrowIfNull(httpClientFactory);

			this.httpClientFactory = httpClientFactory;
		}

		public Task<McpToolCallResult> ExecuteAsync(McpToolDescriptor tool, string argumentsJson, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(tool);
			ArgumentNullException.ThrowIfNull(argumentsJson);

			return tool.Name switch
			{
				"file_read" => ExecuteFileReadAsync(argumentsJson, cancellationToken),
				"file_write" => ExecuteFileWriteAsync(argumentsJson, cancellationToken),
				"run_command" => ExecuteRunCommandAsync(argumentsJson, cancellationToken),
				"search_web" => ExecuteSearchWebAsync(argumentsJson, cancellationToken),
				_ => throw new NotSupportedException($"Built-in tool '{tool.Name}' is not supported."),
			};
		}

		static async Task<McpToolCallResult> ExecuteFileReadAsync(string argumentsJson, CancellationToken cancellationToken)
		{
			var args = ParseArguments(argumentsJson);
			var path = RequireString(args, "path");

			var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
			var truncated = content.Length > MaxOutputLength ? content[..MaxOutputLength] + "\n[truncated]" : content;
			return Success(truncated);
		}

		static async Task<McpToolCallResult> ExecuteFileWriteAsync(string argumentsJson, CancellationToken cancellationToken)
		{
			var args = ParseArguments(argumentsJson);
			var path = RequireString(args, "path");
			var content = RequireString(args, "content");

			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
			return Success($"Wrote {content.Length} characters to '{path}'.");
		}

		static async Task<McpToolCallResult> ExecuteRunCommandAsync(string argumentsJson, CancellationToken cancellationToken)
		{
			var args = ParseArguments(argumentsJson);
			var command = RequireString(args, "command");
			var workingDirectory = args["working_directory"]?.GetValue<string>();

			var psi = new ProcessStartInfo("cmd.exe", "/C " + command)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
			};

			using var process = Process.Start(psi)
				?? throw new InvalidOperationException("Failed to start process.");

			var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

			var stdout = await outputTask.ConfigureAwait(false);
			var stderr = await errorTask.ConfigureAwait(false);
			var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + "\nSTDERR:\n" + stderr;
			var trimmed = combined.Length > MaxOutputLength ? combined[..MaxOutputLength] + "\n[truncated]" : combined;
			return Success(string.IsNullOrWhiteSpace(trimmed) ? "(no output)" : trimmed);
		}

		async Task<McpToolCallResult> ExecuteSearchWebAsync(string argumentsJson, CancellationToken cancellationToken)
		{
			var args = ParseArguments(argumentsJson);
			var rawUrl = RequireString(args, "url");

			if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
				|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				throw new InvalidOperationException("search_web requires an absolute HTTP or HTTPS URL.");
			}

			var client = httpClientFactory.CreateClient("builtin-search-web");
			var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var trimmed = text.Length > MaxOutputLength ? text[..MaxOutputLength] + "\n[truncated]" : text;
			return Success(trimmed);
		}

		static JsonObject ParseArguments(string argumentsJson)
		{
			if (string.IsNullOrWhiteSpace(argumentsJson))
			{
				return new JsonObject();
			}

			try
			{
				return JsonNode.Parse(argumentsJson)?.AsObject() ?? new JsonObject();
			}
			catch (JsonException ex)
			{
				throw new InvalidOperationException("Tool arguments must be valid JSON.", ex);
			}
		}

		static string RequireString(JsonObject args, string key)
		{
			var value = args[key]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new InvalidOperationException($"Required argument '{key}' is missing or empty.");
			}

			return value;
		}

		static McpToolCallResult Success(string text) => new McpToolCallResult
		{
			Succeeded = true,
			DisplayText = text,
			RawJson = "{}",
		};
	}
}
