using System.Text;
using DatabaseSync.Models;

namespace DatabaseSync.Data;

public static class SqlBuilder
{
    public static string QuoteIdentifier(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    public static string BuildSelect(TableSchema schema)
    {
        var columns = string.Join(", ", schema.Columns.Select(c => QuoteIdentifier(c.Name)));
        var order = string.Join(", ", schema.PrimaryKey.Select(c => QuoteIdentifier(c.Name)));
        return $"SELECT {columns} FROM {QuoteIdentifier(schema.Name)} ORDER BY {order}";
    }

    public static string BuildUpsert(TableSchema schema, int rowCount)
    {
        if (rowCount < 1) throw new ArgumentOutOfRangeException(nameof(rowCount));
        var names = string.Join(", ", schema.Columns.Select(c => QuoteIdentifier(c.Name)));
        var sql = new StringBuilder($"INSERT INTO {QuoteIdentifier(schema.Name)} ({names}) VALUES ");
        for (var row = 0; row < rowCount; row++)
        {
            if (row > 0) sql.Append(", ");
            sql.Append('(');
            sql.AppendJoin(", ", schema.Columns.Select((_, col) => $"@p{row}_{col}"));
            sql.Append(')');
        }

        var updateColumns = schema.Columns.Where(c => !c.IsPrimaryKey && !c.IsAutoIncrement).ToArray();
        sql.Append(" ON DUPLICATE KEY UPDATE ");
        if (updateColumns.Length == 0)
        {
            var primary = QuoteIdentifier(schema.PrimaryKey[0].Name);
            sql.Append($"{primary} = {primary}");
        }
        else
        {
            sql.AppendJoin(", ", updateColumns.Select(c =>
                $"{QuoteIdentifier(c.Name)} = VALUES({QuoteIdentifier(c.Name)})"));
        }
        return sql.ToString();
    }
}
