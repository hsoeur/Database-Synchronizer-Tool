using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DatabaseSync.Configuration;
using DatabaseSync.Data;
using DatabaseSync.Models;
using DatabaseSync.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DatabaseSync.Services;

public sealed class ImportService(
    IOptions<SyncOptions> options,
    ISchemaReader schemaReader,
    PackageValidator validator,
    IBackupService backupService,
    ILogger<ImportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ImportReport> ImportAsync(string packagePath, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var settings = options.Value;
        settings.Validate();
        packagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Package introuvable.", packagePath);

        using var archive = ZipFile.OpenRead(packagePath);
        logger.LogInformation("Vérification du package {Package}", packagePath);
        var manifest = await validator.ValidateAsync(archive, cancellationToken);
        EnsureTablesAllowed(manifest, settings.Tables);
        if (settings.BackupBeforeImport) await backupService.CreateAsync(cancellationToken);

        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var schemas = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in manifest.Tables)
        {
            var destinationSchema = await schemaReader.ReadAsync(connection, table.Name, cancellationToken);
            EnsureCompatible(table.Schema, destinationSchema);
            schemas.Add(table.Name, destinationSchema);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long total = 0;
        try
        {
            foreach (var table in manifest.Tables)
            {
                var entry = archive.GetEntry(table.File)!;
                await using var stream = entry.Open();
                var count = await ImportTableAsync(
                    stream, schemas[table.Name], connection, transaction,
                    settings.BatchSize, settings.CommandTimeoutSeconds, cancellationToken);
                if (count != table.RecordCount)
                    throw new InvalidDataException(
                        $"{table.Name}: le manifeste annonce {table.RecordCount} lignes, {count} ont été lues.");
                total += count;
                logger.LogInformation("{Table}: {Count} enregistrements traités", table.Name, count);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError("Import annulé : transaction restaurée.");
            throw;
        }

        watch.Stop();
        return new ImportReport(packagePath, total, manifest.Tables.Count, watch.Elapsed);
    }

    private static async Task<long> ImportTableAsync(
        Stream stream, TableSchema schema, MySqlConnection connection, MySqlTransaction transaction,
        int batchSize, int timeout, CancellationToken cancellationToken)
    {
        var batch = new List<JsonElement>(batchSize);
        long count = 0;
        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
                           stream, JsonOptions, cancellationToken))
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Une ligne de {schema.Name} n'est pas un objet JSON.");
            batch.Add(row.Clone());
            if (batch.Count == batchSize)
            {
                await ExecuteBatchAsync(batch, schema, connection, transaction, timeout, cancellationToken);
                count += batch.Count;
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            await ExecuteBatchAsync(batch, schema, connection, transaction, timeout, cancellationToken);
            count += batch.Count;
        }
        return count;
    }

    private static async Task ExecuteBatchAsync(
        IReadOnlyList<JsonElement> rows, TableSchema schema, MySqlConnection connection,
        MySqlTransaction transaction, int timeout, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(SqlBuilder.BuildUpsert(schema, rows.Count), connection, transaction)
        {
            CommandTimeout = timeout
        };
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            for (var columnIndex = 0; columnIndex < schema.Columns.Count; columnIndex++)
            {
                var column = schema.Columns[columnIndex];
                if (!rows[rowIndex].TryGetProperty(column.Name, out var value))
                    throw new InvalidDataException($"Colonne {schema.Name}.{column.Name} absente du package.");
                command.Parameters.AddWithValue(
                    $"@p{rowIndex}_{columnIndex}", JsonValueConverter.ToDatabaseValue(value, column));
            }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureTablesAllowed(SyncManifest manifest, IReadOnlyCollection<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        var forbidden = manifest.Tables.Where(t => !allowedSet.Contains(t.Name)).Select(t => t.Name).ToArray();
        if (forbidden.Length > 0)
            throw new InvalidDataException($"Tables non autorisées par la configuration : {string.Join(", ", forbidden)}");
    }

    private static void EnsureCompatible(TableSchema source, TableSchema destination)
    {
        if (!source.PrimaryKey.Select(c => c.Name).SequenceEqual(
                destination.PrimaryKey.Select(c => c.Name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"La clé primaire de {source.Name} diffère entre le package et la destination.");
        foreach (var column in source.Columns)
        {
            var target = destination.Columns.FirstOrDefault(c =>
                c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null || !target.DataType.Equals(column.DataType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Schéma incompatible pour {source.Name}.{column.Name}.");
        }
        if (source.Columns.Count != destination.Columns.Count)
            throw new InvalidDataException($"Le nombre de colonnes de {source.Name} diffère.");
    }
}
