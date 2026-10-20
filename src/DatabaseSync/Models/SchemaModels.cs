namespace DatabaseSync.Models;

public sealed record ColumnSchema(
    string Name,
    string DataType,
    string ColumnType,
    int Ordinal,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsAutoIncrement);

public sealed record TableSchema(string Name, IReadOnlyList<ColumnSchema> Columns)
{
    public IReadOnlyList<ColumnSchema> PrimaryKey =>
        Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.Ordinal).ToArray();
}

public sealed record TableManifest(
    string Name,
    string File,
    long RecordCount,
    string ChecksumSha256,
    TableSchema Schema);

public sealed record SyncManifest(
    string Version,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<TableManifest> Tables);

public sealed record ImportReport(string Package, long RowsProcessed, int TablesProcessed, TimeSpan Duration);
public sealed record CompareTableResult(string Table, long Missing, long Different, long Identical);
