namespace DatabaseSync.Configuration;

public sealed class SyncOptions
{
    public const string SectionName = "DatabaseSync";
    public string ConnectionString { get; init; } = "";
    public string CompareConnectionString { get; init; } = "";
    public string[] Tables { get; init; } = [];
    public string OutputDirectory { get; init; } = "packages";
    public string BackupDirectory { get; init; } = "backups";
    public int BatchSize { get; init; } = 500;
    public int CommandTimeoutSeconds { get; init; } = 300;
    public string MySqlDumpPath { get; init; } = "mysqldump";
    public bool BackupBeforeImport { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("DatabaseSync:ConnectionString est obligatoire.");
        if (Tables.Length == 0)
            throw new InvalidOperationException("DatabaseSync:Tables doit contenir au moins une table.");
        if (BatchSize is < 1 or > 5000)
            throw new InvalidOperationException("DatabaseSync:BatchSize doit être compris entre 1 et 5000.");
        if (Tables.Any(t => string.IsNullOrWhiteSpace(t)))
            throw new InvalidOperationException("Les noms de tables ne peuvent pas être vides.");
    }
}
