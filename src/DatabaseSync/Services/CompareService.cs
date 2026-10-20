using System.Security.Cryptography;
using System.Text.Json;
using DatabaseSync.Configuration;
using DatabaseSync.Data;
using DatabaseSync.Models;
using DatabaseSync.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DatabaseSync.Services;

public sealed class CompareService(
    IOptions<SyncOptions> options,
    ISchemaReader schemaReader,
    ILogger<CompareService> logger)
{
    public async Task<IReadOnlyList<CompareTableResult>> CompareAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        settings.Validate();
        if (string.IsNullOrWhiteSpace(settings.CompareConnectionString))
            throw new InvalidOperationException("DatabaseSync:CompareConnectionString est obligatoire pour compare.");

        await using var source = new MySqlConnection(settings.ConnectionString);
        await using var destination = new MySqlConnection(settings.CompareConnectionString);
        await Task.WhenAll(source.OpenAsync(cancellationToken), destination.OpenAsync(cancellationToken));
        var results = new List<CompareTableResult>();
        foreach (var table in settings.Tables.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sourceSchema = await schemaReader.ReadAsync(source, table, cancellationToken);
            var targetSchema = await schemaReader.ReadAsync(destination, table, cancellationToken);
            EnsureComparable(sourceSchema, targetSchema);
            var result = await CompareTableAsync(source, destination, sourceSchema, cancellationToken);
            results.Add(result);
            logger.LogInformation(
                "{Table}: {Identical} identiques, {Different} différentes, {Missing} manquantes",
                table, result.Identical, result.Different, result.Missing);
        }
        return results;
    }

    private static async Task<CompareTableResult> CompareTableAsync(
        MySqlConnection source, MySqlConnection destination, TableSchema schema,
        CancellationToken cancellationToken)
    {
        await using var sourceCommand = new MySqlCommand(SqlBuilder.BuildSelect(schema), source);
        await using var targetCommand = new MySqlCommand(SqlBuilder.BuildSelect(schema), destination);
        await using var left = await sourceCommand.ExecuteReaderAsync(
            System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        await using var right = await targetCommand.ExecuteReaderAsync(
            System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        var hasLeft = await left.ReadAsync(cancellationToken);
        var hasRight = await right.ReadAsync(cancellationToken);
        long missing = 0, different = 0, identical = 0;
        while (hasLeft || hasRight)
        {
            if (!hasRight) { missing++; hasLeft = await left.ReadAsync(cancellationToken); continue; }
            if (!hasLeft) { hasRight = await right.ReadAsync(cancellationToken); continue; }
            var comparison = CompareKeys(left, right, schema);
            if (comparison < 0) { missing++; hasLeft = await left.ReadAsync(cancellationToken); }
            else if (comparison > 0) { hasRight = await right.ReadAsync(cancellationToken); }
            else
            {
                if (RowsEqual(left, right, schema)) identical++; else different++;
                hasLeft = await left.ReadAsync(cancellationToken);
                hasRight = await right.ReadAsync(cancellationToken);
            }
        }
        return new CompareTableResult(schema.Name, missing, different, identical);
    }

    private static int CompareKeys(MySqlDataReader left, MySqlDataReader right, TableSchema schema)
    {
        foreach (var key in schema.PrimaryKey)
        {
            var ordinal = schema.Columns
                .Select((column, index) => (column, index))
                .First(item => item.column.Name.Equals(key.Name, StringComparison.OrdinalIgnoreCase))
                .index;
            var a = left.GetValue(ordinal);
            var b = right.GetValue(ordinal);
            var result = CompareValues(a, b);
            if (result != 0) return result;
        }
        return 0;
    }

    private static int CompareValues(object left, object right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
        if (left.GetType() == right.GetType() && left is IComparable comparable)
            return comparable.CompareTo(right);
        return string.CompareOrdinal(
            Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool RowsEqual(MySqlDataReader left, MySqlDataReader right, TableSchema schema)
    {
        for (var i = 0; i < schema.Columns.Count; i++)
        {
            var a = left.GetValue(i);
            var b = right.GetValue(i);
            if (a is byte[] ba && b is byte[] bb)
            {
                if (!ba.AsSpan().SequenceEqual(bb)) return false;
            }
            else if (!Equals(a, b)) return false;
        }
        return true;
    }

    private static void EnsureComparable(TableSchema source, TableSchema target)
    {
        var left = source.Columns.Select(c => (c.Name.ToLowerInvariant(), c.DataType.ToLowerInvariant()));
        var right = target.Columns.Select(c => (c.Name.ToLowerInvariant(), c.DataType.ToLowerInvariant()));
        if (!left.SequenceEqual(right))
            throw new InvalidDataException($"Les schémas de {source.Name} ne sont pas comparables.");
    }
}
