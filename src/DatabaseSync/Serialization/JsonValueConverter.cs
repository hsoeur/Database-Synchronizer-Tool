using System.Globalization;
using System.Text.Json;
using DatabaseSync.Models;

namespace DatabaseSync.Serialization;

public static class JsonValueConverter
{
    private static readonly HashSet<string> BinaryTypes =
        new(StringComparer.OrdinalIgnoreCase) { "binary", "varbinary", "tinyblob", "blob", "mediumblob", "longblob" };

    public static void Write(Utf8JsonWriter writer, string propertyName, object value)
    {
        writer.WritePropertyName(propertyName);
        switch (value)
        {
            case DBNull: writer.WriteNullValue(); break;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case byte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case float number: writer.WriteNumberValue(number); break;
            case double number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case DateTime date: writer.WriteStringValue(date.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan time: writer.WriteStringValue(time.ToString("c", CultureInfo.InvariantCulture)); break;
            case Guid guid: writer.WriteStringValue(guid); break;
            default: writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }

    public static object ToDatabaseValue(JsonElement element, ColumnSchema column)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return DBNull.Value;
        if (BinaryTypes.Contains(column.DataType))
            return Convert.FromBase64String(element.GetString() ?? "");

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when IsInteger(column.DataType) && column.ColumnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase)
                => element.GetUInt64(),
            JsonValueKind.Number when IsInteger(column.DataType) => element.GetInt64(),
            JsonValueKind.Number when column.DataType is "float" or "double" => element.GetDouble(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String => element.GetString()!,
            _ => element.GetRawText()
        };
    }

    private static bool IsInteger(string type) =>
        type is "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint" or "year" or "bit";
}
