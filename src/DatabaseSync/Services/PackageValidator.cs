using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DatabaseSync.Models;

namespace DatabaseSync.Services;

public sealed class PackageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SyncManifest> ValidateAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("manifest.json est absent du package.");
        SyncManifest manifest;
        await using (var stream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<SyncManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Le manifeste est vide ou invalide.");

        if (manifest.Version != "1.0") throw new InvalidDataException($"Version de package non prise en charge : {manifest.Version}");
        if (manifest.Tables.Count == 0) throw new InvalidDataException("Le manifeste ne contient aucune table.");
        if (manifest.Tables.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Tables.Count)
            throw new InvalidDataException("Le manifeste contient des tables en double.");

        foreach (var table in manifest.Tables)
        {
            if (table.Schema.PrimaryKey.Count == 0)
                throw new InvalidDataException($"La table {table.Name} n'a pas de clé primaire déclarée.");
            var entry = archive.GetEntry(table.File)
                ?? throw new InvalidDataException($"Le fichier {table.File} est absent.");
            await using var stream = entry.Open();
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual), Convert.FromHexString(table.ChecksumSha256)))
                throw new InvalidDataException($"Checksum invalide pour {table.File}.");
        }
        return manifest;
    }
}
