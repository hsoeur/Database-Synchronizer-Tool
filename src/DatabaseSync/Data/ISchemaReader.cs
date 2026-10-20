using MySqlConnector;
using DatabaseSync.Models;

namespace DatabaseSync.Data;

public interface ISchemaReader
{
    Task<TableSchema> ReadAsync(MySqlConnection connection, string table, CancellationToken cancellationToken);
}
