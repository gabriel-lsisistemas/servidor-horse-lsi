using System.Globalization;
using System.Text.Json;
using Dapper;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IContratoService
{
    Task<Dictionary<string, object?>> AtualizaDoCobraAsync(JsonDocument? body, CancellationToken cancellationToken = default);
}

public sealed class ContratoService : IContratoService
{
    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<ContratoService> _logger;

    public ContratoService(ISqlConnectionFactory connections, ILogger<ContratoService> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task<Dictionary<string, object?>> AtualizaDoCobraAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
            return JsonRowHelper.StatusObject("2", "Body JSON obrigatorio.");

        var root = body.RootElement;
        // Alguns clients envelopam em { "data": { ... } }
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            root = dataEl;

        var idClick = ReadInt(root, "id_click", "ID", "id_contrato_click");
        if (idClick <= 0)
            return JsonRowHelper.StatusObject("2", "id_click do contrato (ID no Click) e obrigatorio.");

        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var existe = await conexao.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM CONTRATOS WHERE ID = @idClick",
            new { idClick });

        if (existe == 0)
            return JsonRowHelper.StatusObject("2", $"Contrato ID {idClick} nao encontrado no Click.");

        var sets = new List<string>();
        var param = new DynamicParameters();
        param.Add("idClick", idClick);

        // Forca gravacao mesmo quando o valor e 0 (remove adicional no Click).
        void SetDecimal(string column, params string[] names)
        {
            var value = ReadDecimal(root, names);
            if (value is null)
                return;
            var p = "p_" + column;
            sets.Add($"{column} = @{p}");
            param.Add(p, value.Value);
        }

        void SetString(string column, params string[] names)
        {
            var value = ReadString(root, names);
            if (value is null)
                return;
            var p = "p_" + column;
            sets.Add($"{column} = @{p}");
            param.Add(p, value);
        }

        void SetDate(string column, params string[] names)
        {
            var value = ReadDate(root, names);
            if (value is null)
                return;
            var p = "p_" + column;
            sets.Add($"{column} = @{p}");
            param.Add(p, value.Value);
        }

        // Sempre tenta os campos de adicional primeiro (podem ser 0).
        SetDecimal("QTD_PDV_AD", "QTD_PDV_AD", "qtd_pdv_ad");
        SetDecimal("QTD_USUARIOS_AD", "QTD_USUARIOS_AD", "qtd_usuarios_ad");
        SetDecimal("QTD_BALCAO_AD", "QTD_BALCAO_AD", "qtd_balcao_ad");
        SetDecimal("QTDE_PDV", "QTDE_PDV", "qtd_pdv");
        SetDecimal("QTDE_USUARIOS", "QTDE_USUARIOS", "qtd_usuarios");
        SetDecimal("QTDE_BALCAO", "QTDE_BALCAO", "qtd_balcao");
        SetDecimal("VALOR_CONTRATO", "VALOR_CONTRATO", "total_contrato");
        SetDecimal("VALOR_LICENCA", "VALOR_LICENCA", "total_plano");
        SetDecimal("valor_receber", "valor_receber", "VALOR_CONTRATO", "total_contrato");
        SetString("OBS", "OBS", "observacao");
        SetString("LOCAL_INSTALACAO", "LOCAL_INSTALACAO", "descricao_instalacao");
        SetDate("DATA_VALIDADE", "DATA_VALIDADE", "data_final", "data_validade_local");
        SetDate("PRIMEIRO_VENCIMENTO", "PRIMEIRO_VENCIMENTO", "data_inicial");
        SetString("contrato_assinado", "contrato_assinado");

        var idTipoContrato = ReadInt(root, "ID_TIPO_CONTRATO", "id_tipo_contrato", "id_click_plano");
        if (idTipoContrato > 0)
        {
            var tipoExiste = await conexao.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM TIPO_CONTRATO WHERE ID = @idTipo",
                new { idTipo = idTipoContrato });

            if (tipoExiste == 0)
            {
                return JsonRowHelper.StatusObject(
                    "2",
                    $"ID_TIPO_CONTRATO {idTipoContrato} nao existe em TIPO_CONTRATO no Click.");
            }

            sets.Add("ID_TIPO_CONTRATO = @p_ID_TIPO_CONTRATO");
            param.Add("p_ID_TIPO_CONTRATO", idTipoContrato);
        }

        var temAtivo = await conexao.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'CONTRATOS' AND COLUMN_NAME = 'ATIVO'
            """);
        if (temAtivo > 0)
            SetString("ATIVO", "ATIVO", "ativo");

        var idOriginal = ReadInt(root, "ID_ORIGINAL", "contrato_cliente_id", "id_cobra");
        if (idOriginal > 0)
        {
            sets.Add("ID_ORIGINAL = @p_ID_ORIGINAL");
            param.Add("p_ID_ORIGINAL", idOriginal);
        }

        if (sets.Count == 0)
            return JsonRowHelper.StatusObject("2", "Nenhum campo para atualizar no payload.");

        var sql = $"UPDATE CONTRATOS SET {string.Join(", ", sets)} WHERE ID = @idClick";
        _logger.LogInformation("Contrato atualiza Click ID={Id}: {Sql}", idClick, sql);

        var afetados = await conexao.ExecuteAsync(sql, param);
        if (afetados <= 0)
            return JsonRowHelper.StatusObject("2", "Nenhuma linha atualizada no Click.");

        var atual = await conexao.QueryFirstAsync(
            """
            SELECT ID, ID_TIPO_CONTRATO, QTDE_PDV, QTD_PDV_AD, QTDE_USUARIOS, QTD_USUARIOS_AD,
                   QTDE_BALCAO, QTD_BALCAO_AD, VALOR_CONTRATO, valor_receber, ID_ORIGINAL
            FROM CONTRATOS WHERE ID = @idClick
            """,
            new { idClick });

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "1",
            ["erro"] = "Contrato atualizado no Click.",
            ["id_click"] = idClick,
            ["campos"] = sets.Count,
            ["contrato"] = JsonRowHelper.ToLowerDictionary((object?)atual)
        };
    }

    private static int ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                return n;
            if (int.TryParse(el.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                return n;
        }

        return 0;
    }

    private static decimal? ReadDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            var raw = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            raw = raw?.Trim();
            if (string.IsNullOrEmpty(raw))
                continue;

            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;

            // pt-BR: 1.180,50
            var br = raw.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(br, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                return d;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            s = s?.Trim();
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        return null;
    }

    private static DateTime? ReadDate(JsonElement root, params string[] names)
    {
        var s = ReadString(root, names);
        if (string.IsNullOrEmpty(s))
            return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt.Date;
        if (DateTime.TryParse(s, out dt))
            return dt.Date;
        return null;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement el)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                el = prop.Value;
                return true;
            }
        }

        el = default;
        return false;
    }
}
