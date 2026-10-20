using System.Diagnostics;
using DatabaseSync.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DatabaseSync.Services;

public sealed class MySqlDumpBackupService(
    IOptions<SyncOptions> options,
    ILogger<MySqlDumpBackupService> logger) : IBackupService
{
    public async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cs = new MySqlConnectionStringBuilder(settings.ConnectionString);
        Directory.CreateDirectory(settings.BackupDirectory);
        var safeDatabase = string.Concat(cs.Database.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        var path = Path.GetFullPath(Path.Combine(
            settings.BackupDirectory, $"{safeDatabase}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.sql"));

        var start = new ProcessStartInfo
        {
            FileName = settings.MySqlDumpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--host"); start.ArgumentList.Add(cs.Server);
        start.ArgumentList.Add("--port"); start.ArgumentList.Add(cs.Port.ToString());
        start.ArgumentList.Add("--user"); start.ArgumentList.Add(cs.UserID);
        start.ArgumentList.Add("--single-transaction");
        start.ArgumentList.Add("--routines");
        start.ArgumentList.Add("--triggers");
        start.ArgumentList.Add("--events");
        start.ArgumentList.Add("--databases"); start.ArgumentList.Add(cs.Database);
        start.Environment["MYSQL_PWD"] = cs.Password;

        logger.LogInformation("Création de la sauvegarde complète {Path}", path);
        using var process = new Process { StartInfo = start };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await process.StandardOutput.BaseStream.CopyToAsync(file, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            File.Delete(path);
            throw new InvalidOperationException($"mysqldump a échoué (code {process.ExitCode}) : {error}");
        }

        logger.LogInformation("Sauvegarde terminée : {Path}", path);
        return path;
    }
}
