using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SaasCopilot.Service;

[McpServerToolType]
public sealed class DbContextTools
{
	readonly IDbContextInspectionService dbContextInspectionService;

	public DbContextTools(IDbContextInspectionService dbContextInspectionService)
	{
		this.dbContextInspectionService = dbContextInspectionService;
	}

	[McpServerTool(Name = "inspect_db_context"), Description("Returns the active form's in-memory DataSet: tables, columns, row states, and values. Use to inspect loaded data or pending changes, not for schema questions.")]
	public Task<InspectDbContextResult> InspectDbContextAsync(
		[Description("Form target such as 'active'.")] string form = "active",
		[Description("Optional table name to filter results to a single table. Omit to return all tables.")] string? table = null,
		[Description("Inspection mode. Use summary for table names and row counts only, full for all current column values and row states, or diff to also see original values for modified rows.")] InspectDbContextMode mode = InspectDbContextMode.Full,
		CancellationToken cancellationToken = default)
	{
		return dbContextInspectionService.InspectDbContextAsync(new InspectDbContextRequest(form, table, mode), cancellationToken);
	}

	[McpServerTool(Name = "inspect_db_rows"), Description("Fetches persisted database rows for a table in the active form's entity. Use to compare in-memory state with the database or verify a save.")]
	public Task<InspectDbRowsResult> InspectDbRowsAsync(
		[Description("The table name to fetch live rows for. It must match a table present in the in-memory DataSet.")] string table,
		[Description("Form target such as 'active'.")] string form = "active",
		CancellationToken cancellationToken = default)
	{
		return dbContextInspectionService.InspectDbRowsAsync(new InspectDbRowsRequest(table, form), cancellationToken);
	}
}