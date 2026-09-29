using System.Globalization;
using System.Text.Json;

namespace ServidorHorseLsi.Api.Infrastructure;

public static class JsonRowHelper
{
    public static Dictionary<string, object?> ToLowerDictionary(object? row)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (row is null)
            return dict;

        // Dapper returns DapperRow as IDictionary<string, object>
        if (row is IDictionary<string, object> dapperRow)
        {
            foreach (var kv in dapperRow)
                dict[kv.Key.ToLowerInvariant()] = NormalizeValue(kv.Value);
            return dict;
        }

        if (row is IDictionary<string, object?> nullableRow)
        {
            foreach (var kv in nullableRow)
                dict[kv.Key.ToLowerInvariant()] = NormalizeValue(kv.Value);
            return dict;
        }

        foreach (var prop in row.GetType().GetProperties())
        {
            if (!prop.CanRead)
                continue;
            dict[prop.Name.ToLowerInvariant()] = NormalizeValue(prop.GetValue(row));
        }

        return dict;
    }

    public static List<Dictionary<string, object?>> ToLowerList(IEnumerable<object> rows) =>
        rows.Select(r => ToLowerDictionary((object?)r)).ToList();

    /// <param name="row">Use (object?) quando vier de consulta Dapper (dynamic).</param>
    public static Dictionary<string, object?> MergeRow(object? row, params (string Key, object? Value)[] extras)
    {
        var dict = ToLowerDictionary(row);

        foreach (var (key, value) in extras)
            dict[key.ToLowerInvariant()] = value;

        return dict;
    }

    public static Dictionary<string, object?> StatusObject(string status, string erro) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = status,
            ["erro"] = erro
        };

    /// <summary>
    /// Matches Horse/DataSet.Serialize JSON: ISO-8601 UTC dates, numeric types unchanged, null preserved.
    /// </summary>
    private static object? NormalizeValue(object? value)
    {
        if (value is null or DBNull)
            return null;

        if (value is DateTime dt)
            return FormatDateTimeAsUtcIso(dt);

        if (value is DateTimeOffset dto)
            return FormatDateTimeAsUtcIso(dto.UtcDateTime);

        if (value is bool)
            return value;

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return value;

        return value;
    }

    internal static string FormatDateTimeAsUtcIso(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public static string SerializeIso88591(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonSerializerOptions);
        return json;
    }

    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };
}
