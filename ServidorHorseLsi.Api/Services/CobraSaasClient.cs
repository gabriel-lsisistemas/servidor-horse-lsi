using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ServidorHorseLsi.Api.Configuration;

namespace ServidorHorseLsi.Api.Services;

public interface ICobraSaasClient
{
    Task<CobraLoteResultado> EnviarPlanosAsync(IReadOnlyList<Dictionary<string, object?>> planos, CancellationToken cancellationToken);
    Task<CobraLoteResultado> EnviarClientesAsync(IReadOnlyList<Dictionary<string, object?>> clientes, CancellationToken cancellationToken);
    Task<CobraLoteResultado> EnviarContratosAsync(IReadOnlyList<Dictionary<string, object?>> contratos, CancellationToken cancellationToken);
    Task<CobraLoteResultado> EnviarContasAsync(IReadOnlyList<Dictionary<string, object?>> contas, CancellationToken cancellationToken);
}

public sealed class CobraLoteResultado
{
    public bool Sucesso { get; init; }
    public string? Erro { get; init; }
    public int Total { get; init; }
    public int Processados { get; init; }
    public int Criados { get; init; }
    public int Atualizados { get; init; }
    public int Erros { get; init; }
    public List<Dictionary<string, object?>> Falhas { get; init; } = [];
    public List<Dictionary<string, object?>> Registros { get; init; } = [];
}

public sealed class CobraSaasClient : ICobraSaasClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly CobraSaasOptions _options;

    public CobraSaasClient(HttpClient http, IOptions<CobraSaasOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<CobraLoteResultado> EnviarPlanosAsync(
        IReadOnlyList<Dictionary<string, object?>> planos,
        CancellationToken cancellationToken) =>
        EnviarAsync("receberPlanos", "planos", planos, cancellationToken);

    public Task<CobraLoteResultado> EnviarClientesAsync(
        IReadOnlyList<Dictionary<string, object?>> clientes,
        CancellationToken cancellationToken) =>
        EnviarAsync("receberClientes", "clientes", clientes, cancellationToken);

    public Task<CobraLoteResultado> EnviarContratosAsync(
        IReadOnlyList<Dictionary<string, object?>> contratos,
        CancellationToken cancellationToken) =>
        EnviarAsync("receberContratos", "contratos", contratos, cancellationToken);

    public Task<CobraLoteResultado> EnviarContasAsync(
        IReadOnlyList<Dictionary<string, object?>> contas,
        CancellationToken cancellationToken) =>
        EnviarAsync("receberContas", "contas", contas, cancellationToken);

    private async Task<CobraLoteResultado> EnviarAsync(
        string method,
        string listaNome,
        IReadOnlyList<Dictionary<string, object?>> itens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("CobraSaas:BaseUrl nao configurado.");

        if (string.IsNullOrWhiteSpace(_options.RestKey))
            throw new InvalidOperationException("CobraSaas:RestKey nao configurado.");

        var url = BuildUrl(method);
        var body = new Dictionary<string, object?>
        {
            ["system_unit_id"] = _options.SystemUnitId,
            [listaNome] = itens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _options.RestKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new CobraLoteResultado
            {
                Sucesso = false,
                Erro = $"HTTP {(int)response.StatusCode}: {json}"
            };
        }

        return ParseEnvelope(json);
    }

    private string BuildUrl(string method)
    {
        var baseUrl = _options.BaseUrl.Trim();
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}class=LsiClickRestService&method={method}";
    }

    private static CobraLoteResultado ParseEnvelope(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        var data = root.TryGetProperty("data", out var dataEl) ? dataEl : root;

        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            var erro = data.ValueKind == JsonValueKind.String
                ? data.GetString()
                : json;
            return new CobraLoteResultado { Sucesso = false, Erro = erro };
        }

        if (data.ValueKind != JsonValueKind.Object)
            return new CobraLoteResultado { Sucesso = true };

        return new CobraLoteResultado
        {
            Sucesso = true,
            Total = ReadInt(data, "total"),
            Processados = ReadInt(data, "processados"),
            Criados = ReadInt(data, "criados"),
            Atualizados = ReadInt(data, "atualizados"),
            Erros = ReadInt(data, "erros"),
            Falhas = ReadObjectList(data, "falhas"),
            Registros = ReadObjectList(data, "registros")
        };
    }

    private static int ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return 0;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;

        return int.TryParse(el.GetString(), out n) ? n : 0;
    }

    private static List<Dictionary<string, object?>> ReadObjectList(JsonElement obj, string name)
    {
        var list = new List<Dictionary<string, object?>>();
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.EnumerateObject())
                dict[prop.Name] = JsonValue(prop.Value);
            list.Add(dict);
        }

        return list;
    }

    private static object? JsonValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
        JsonValueKind.String => el.GetString(),
        _ => el.GetRawText()
    };
}
