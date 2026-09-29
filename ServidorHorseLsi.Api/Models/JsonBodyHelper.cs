using System.Text.Json;

namespace ServidorHorseLsi.Api.Models;

public static class JsonBodyHelper
{
    public static async Task<string?> GetStringAsync(HttpRequest request, string propertyName)
    {
        var body = await ReadBodyAsync(request);
        if (body is null)
            return null;

        if (body.RootElement.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.GetRawText(),
                _ => prop.ToString()
            };
        }

        return null;
    }

    public static async Task<int> GetIntAsync(HttpRequest request, string propertyName, int defaultValue = 0)
    {
        var text = await GetStringAsync(request, propertyName);
        return int.TryParse(text, out var value) ? value : defaultValue;
    }

    public static async Task<JsonDocument?> ReadBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        if (request.ContentLength == 0 && !request.Body.CanRead)
            return null;

        try
        {
            return await JsonDocument.ParseAsync(request.Body);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    public static async Task<object?> ReadBodyAsObjectAsync(HttpRequest request)
    {
        var doc = await ReadBodyAsync(request);
        return doc is null ? new { } : JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
    }
}
