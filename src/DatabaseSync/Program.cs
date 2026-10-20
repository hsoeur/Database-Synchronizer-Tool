using DatabaseSync.Configuration;
using DatabaseSync.Data;
using DatabaseSync.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "DBSYNC_");
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddSingleton<ISchemaReader, MySqlSchemaReader>();
builder.Services.AddSingleton<IBackupService, MySqlDumpBackupService>();
builder.Services.AddSingleton<PackageValidator>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<ImportService>();
builder.Services.AddSingleton<CompareService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("dbsync");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };

try
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    switch (args[0].ToLowerInvariant())
    {
        case "export":
            {
                var path = await host.Services.GetRequiredService<ExportService>()
                    .ExportAsync(args.ElementAtOrDefault(1), cancellation.Token);
                Console.WriteLine($"Export réussi : {path}");
                break;
            }
        case "import":
            {
                if (args.Length != 2) throw new ArgumentException("Usage : dbsync import <package.zip>");
                var report = await host.Services.GetRequiredService<ImportService>()
                    .ImportAsync(args[1], cancellation.Token);
                Console.WriteLine(
                    $"Import réussi : {report.TablesProcessed} table(s), {report.RowsProcessed} ligne(s), {report.Duration:g}");
                break;
            }
        case "compare":
            {
                var results = await host.Services.GetRequiredService<CompareService>()
                    .CompareAsync(cancellation.Token);
                Console.WriteLine("Table\tManquantes\tDifférentes\tIdentiques");
                foreach (var result in results)
                    Console.WriteLine($"{result.Table}\t{result.Missing}\t{result.Different}\t{result.Identical}");
                break;
            }
        default:
            throw new ArgumentException($"Commande inconnue : {args[0]}. Utilisez --help.");
    }
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Opération annulée.");
    return 130;
}
catch (Exception exception)
{
    logger.LogError(exception, "Échec de l'opération.");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        DatabaseSync - synchronisation MySQL hors ligne

        Commandes :
          dbsync export [package.zip]   Exporte les tables configurées
          dbsync import <package.zip>   Sauvegarde puis importe par UPSERT
          dbsync compare                Compare les deux bases configurées

        Configuration : appsettings.json ou variables DBSYNC_DatabaseSync__...
        """);
}
