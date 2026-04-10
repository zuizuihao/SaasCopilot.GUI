namespace SaasCopilot.Copilot.GUI.Features.Mcp
{
	public sealed class AssistantModelDescriptor
	{
		public required string Id { get; init; }

		public required string DisplayName { get; init; }

		public string? Description { get; init; }

		/// <summary>
		/// Maximum context window in tokens as reported by LM Studio, or <see langword="null"/> when unknown.
		/// </summary>
		public int? ContextWindow { get; init; }
	}
}
