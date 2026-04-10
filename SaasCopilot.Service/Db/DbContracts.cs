namespace SaasCopilot.Service;

public enum InspectDbContextMode
{
	Summary,
	Full,
	Diff,
}

public sealed record InspectDbContextRequest(
	string Form = "active",
	string? Table = null,
	InspectDbContextMode Mode = InspectDbContextMode.Full);

public sealed record DbRowDescription(
	string RowState,
	IReadOnlyDictionary<string, object?> CurrentValues,
	IReadOnlyDictionary<string, object?>? OriginalValues = null);

public sealed record DbTableDescription(
	string TableName,
	int TotalRows,
	int AddedRows,
	int ModifiedRows,
	int DeletedRows,
	IReadOnlyList<string> Columns,
	IReadOnlyList<DbRowDescription>? Rows = null);

public sealed record InspectDbContextResult(
	bool Succeeded,
	string Status,
	InspectDbContextMode Mode,
	string? FormName = null,
	string? FormType = null,
	string? BusinessEntityType = null,
	IReadOnlyList<DbTableDescription>? Tables = null,
	string? Message = null,
	string? StackTrace = null)
{
	public static InspectDbContextResult Success(
		string formName,
		string formType,
		string businessEntityType,
		InspectDbContextMode mode,
		IReadOnlyList<DbTableDescription> tables)
		=> new(true, "success", mode, formName, formType, businessEntityType, tables);

	public static InspectDbContextResult Failure(string status, string message, InspectDbContextMode mode, string? stackTrace = null)
		=> new(false, status, mode, Message: message, StackTrace: stackTrace);
}

public sealed record InspectDbRowsRequest(
	string Table,
	string Form = "active");

public sealed record InspectDbRowsResult(
	bool Succeeded,
	string Status,
	string? Table = null,
	IReadOnlyList<string>? Columns = null,
	IReadOnlyList<DbRowDescription>? Rows = null,
	string? Message = null,
	string? StackTrace = null)
{
	public static InspectDbRowsResult Success(
		string table,
		IReadOnlyList<string> columns,
		IReadOnlyList<DbRowDescription> rows)
		=> new(true, "success", table, columns, rows);

	public static InspectDbRowsResult Failure(string status, string message, string? stackTrace = null)
		=> new(false, status, Message: message, StackTrace: stackTrace);
}

public interface IDbContextInspectionService
{
	Task<InspectDbContextResult> InspectDbContextAsync(InspectDbContextRequest request, CancellationToken cancellationToken = default);

	Task<InspectDbRowsResult> InspectDbRowsAsync(InspectDbRowsRequest request, CancellationToken cancellationToken = default);
}