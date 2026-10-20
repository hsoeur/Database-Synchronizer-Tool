using System.IO.Compression;
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

public sealed class ExportService(
    IOptions<SyncOptions> options,
    ISchemaReader schemaReader,
    ILogger<ExportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> ExportAsync(string? requestedPath, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        settings.Validate();
        Directory.CreateDirectory(settings.OutputDirectory);
        var path = Path.GetFullPath(requestedPath ?? Path.Combine(
            settings.OutputDirectory, $"Sync_{DateTime.Now:yyyyMMdd_HHmmss}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new IOException($"Le package existe déjà : {path}");

        await using var connection = new MySqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var manifests = new List<TableManifest>();
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);
            foreach (var table in settings.Tables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var schema = await schemaReader.ReadAsync(connection, table, cancellationToken);
                var result = await WriteTableAsync(archive, connection, schema, settings.CommandTimeoutSeconds, cancellationToken);
                manifests.Add(result);
                logger.LogInformation("{Table}: {Count} enregistrements exportés", table, result.RecordCount);
            }

            var manifest = new SyncManifest("1.0", DateTimeOffset.UtcNow, manifests);
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        logger.LogInformation("Package créé : {Path}", path);
        return path;
    }

    private static async Task<TableManifest> WriteTableAsync(
        ZipArchive archive, MySqlConnection connection, TableSchema schema,
        int timeout, CancellationToken cancellationToken)
    {
        var fileName = $"{schema.Name}.json";
        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var zipStream = entry.Open();
        await using var hashingStream = new HashingWriteStream(zipStream, hash);
        await using var command = new MySqlCommand(SqlBuilder.BuildSelect(schema), connection)
        {
            CommandTimeout = timeout
        };
        await using var reader = await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.SequentialAccess, cancellationToken);
        long count = 0;
        using (var writer = new Utf8JsonWriter(hashingStream))
        {
            writer.WriteStartArray();
            while (await reader.ReadAsync(cancellationToken))
            {
                writer.WriteStartObject();
                for (var i = 0; i < schema.Columns.Count; i++)
                    JsonValueConverter.Write(writer, schema.Columns[i].Name, reader.GetValue(i));
                writer.WriteEndObject();
                count++;
                if (count % 1000 == 0) await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);
        }
        var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new TableManifest(schema.Name, fileName, count, checksum, schema);
    }
}

internal sealed class HashingWriteStream(Stream inner, IncrementalHash hash) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count)
    {
        hash.AppendData(buffer, offset, count);
        inner.Write(buffer, offset, count);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        hash.AppendData(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
