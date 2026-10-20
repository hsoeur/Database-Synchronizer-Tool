namespace DatabaseSync.Services;

public interface IBackupService
{
    Task<string> CreateAsync(CancellationToken cancellationToken);
}
