using DatabaseSync.Models;
using MySqlConnector;

namespace DatabaseSync.Data;

public sealed class MySqlSchemaReader : ISchemaReader
{
    public async Task<TableSchema> ReadAsync(
        MySqlConnection connection, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, ORDINAL_POSITION,
                   IS_NULLABLE, COLUMN_KEY, EXTRA
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnSchema>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnSchema(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4).Equals("YES", StringComparison.OrdinalIgnoreCase),
                reader.GetString(5).Equals("PRI", StringComparison.OrdinalIgnoreCase),
                reader.GetString(6).Contains("auto_increment", StringComparison.OrdinalIgnoreCase)));
        }

        if (columns.Count == 0)
            throw new InvalidOperationException($"La table '{table}' n'existe pas dans la base.");
        if (!columns.Any(c => c.IsPrimaryKey))
            throw new InvalidOperationException($"La table '{table}' ne possède pas de clé primaire.");

        return new TableSchema(table, columns);
    }
}
