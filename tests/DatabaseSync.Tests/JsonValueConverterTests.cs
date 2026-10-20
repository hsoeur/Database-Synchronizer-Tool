using System.Text;
using System.Text.Json;
using DatabaseSync.Models;
using DatabaseSync.Serialization;

namespace DatabaseSync.Tests;

public sealed class JsonValueConverterTests
{
    [Fact]
    public void BinaryRoundTripsAsBase64()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            JsonValueConverter.Write(writer, "Data", new byte[] { 0, 1, 255 });
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var column = new ColumnSchema("Data", "blob", "blob", 1, true, false, false);

        Assert.Equal(new byte[] { 0, 1, 255 },
            JsonValueConverter.ToDatabaseValue(document.RootElement.GetProperty("Data"), column));
    }

    [Fact]
    public void NullBecomesDbNull()
    {
        using var document = JsonDocument.Parse("""{"Value":null}""");
        var column = new ColumnSchema("Value", "varchar", "varchar(10)", 1, true, false, false);
        Assert.Equal(DBNull.Value,
            JsonValueConverter.ToDatabaseValue(document.RootElement.GetProperty("Value"), column));
    }
}
