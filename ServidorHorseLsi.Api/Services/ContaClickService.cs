using System.Data.SqlClient;
using System.Globalization;
using System.Text.Json;
using Dapper;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IContaClickService
{
    Task<Dictionary<string, object?>> AtualizaParcelasDoCobraAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Integração Cobra → Click: apaga parcelas em aberto e gera as novas.
/// Não apaga parcela paga nem parcela vigente Asaas (pay_ futuro).
/// INTEGRADO = N para o Click processar localmente.
/// </summary>
public sealed class ContaClickService : IContaClickService
{
    private const int SituacaoAberto = 1;
    private const int SituacaoPago = 2;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<ContaClickService> _logger;

    public ContaClickService(ISqlConnectionFactory connections, ILogger<ContaClickService> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task<Dictionary<string, object?>> AtualizaParcelasDoCobraAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
            return JsonRowHelper.StatusObject("2", "Body JSON obrigatorio.");

        var root = body.RootElement;
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            root = dataEl;

        var idClickContrato = ReadInt(root, "id_click", "id_click_contrato", "ID_CONTRATO");
        if (idClickContrato <= 0)
            return JsonRowHelper.StatusObject("2", "id_click do contrato e obrigatorio.");

        var parcelas = ReadArray(root, "parcelas", "contas");

        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var contrato = await conexao.QueryFirstOrDefaultAsync(
            "SELECT ID, ID_CLIENTE, ID_ORIGINAL FROM CONTRATOS WHERE ID = @id",
            new { id = idClickContrato });

        if (contrato is null)
            return JsonRowHelper.StatusObject("2", $"Contrato ID {idClickContrato} nao encontrado no Click.");

        var idPessoa = GetDynamicInt(contrato, "ID_CLIENTE");
        var payloadPessoa = ReadInt(root, "id_click_cliente", "ID_PESSOA", "id_pessoa");
        if (payloadPessoa > 0)
            idPessoa = payloadPessoa;

        if (idPessoa <= 0)
            return JsonRowHelper.StatusObject("2", "Contrato sem ID_CLIENTE no Click.");

        var colunas = new ColumnCache(conexao);
        await colunas.LoadAsync("CONTAS_PARCELAS", "CONTAS_DETALHE", "CONTAS_PAGAMENTO");

        var tipoPagamento = await conexao.ExecuteScalarAsync<int?>(
            """
            SELECT TOP 1 ID_TIPO_PAGAMENTO
            FROM CONTAS_PARCELAS
            WHERE ID_CONTRATO = @id AND ID_TIPO_PAGAMENTO IS NOT NULL
            """,
            new { id = idClickContrato });

        await using var tx = conexao.BeginTransaction();
        var resultados = new List<Dictionary<string, object?>>();
        var apagadas = 0;

        try
        {
            var idsPreservar = await ColetarIdsPreservarAsync(conexao, tx, idClickContrato, parcelas);
            apagadas = await ApagarParcelasAbertasAsync(conexao, tx, idClickContrato, idsPreservar);

            foreach (var parcela in parcelas)
            {
                resultados.Add(await GerarParcelaAsync(
                    conexao,
                    tx,
                    colunas,
                    idClickContrato,
                    idPessoa,
                    tipoPagamento,
                    parcela));
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.LogError(ex, "Falha ao gravar parcelas Cobra no Click contrato {Id}", idClickContrato);
            return JsonRowHelper.StatusObject("2", "Falha ao gravar parcelas no Click: " + ex.Message);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "1",
            ["erro"] = "Parcelas recriadas no Click.",
            ["id_click"] = idClickContrato,
            ["apagadas"] = apagadas,
            ["processadas"] = resultados.Count,
            ["parcelas"] = resultados
        };
    }

    private static async Task<HashSet<int>> ColetarIdsPreservarAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        int idContrato,
        List<JsonElement> parcelas)
    {
        var ids = new HashSet<int>();

        var pagas = await conexao.QueryAsync<int>(
            """
            SELECT d.ID
            FROM CONTAS_DETALHE d
            INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
            WHERE p.ID_CONTRATO = @idContrato
              AND d.ID_SITUACAO_PARCELA = @pago
            """,
            new { idContrato, pago = SituacaoPago },
            tx);

        foreach (var id in pagas)
            ids.Add(id);

        foreach (var parcela in parcelas)
        {
            if (!ReadBool(parcela, "preservar"))
                continue;

            var contaId = ReadInt(parcela, "conta_id", "ID_ORIGINAL", "id_cobra");
            var idDetalhePayload = ReadInt(parcela, "id_click", "ID", "id_click_detalhe");
            var vencimento = ReadDate(parcela, "dt_vencimento", "DATA_VENCIMENTO") ?? DateTime.Today;
            var existente = await LocalizarDetalheAsync(
                conexao, tx, idContrato, contaId, idDetalhePayload, vencimento);

            if (existente is not null)
            {
                var id = GetDynamicInt(existente, "ID");
                if (id > 0)
                    ids.Add(id);
            }
        }

        return ids;
    }

    private static async Task<int> ApagarParcelasAbertasAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        int idContrato,
        HashSet<int> idsPreservar)
    {
        var preservar = idsPreservar.Count == 0 ? new[] { 0 } : idsPreservar.ToArray();

        var detalhes = (await conexao.QueryAsync<(int Id, int IdParcelas)>(
            """
            SELECT d.ID AS Id, d.ID_CONTAS_PARCELAS AS IdParcelas
            FROM CONTAS_DETALHE d
            INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
            WHERE p.ID_CONTRATO = @idContrato
              AND d.ID_SITUACAO_PARCELA <> @pago
              AND d.ID NOT IN @preservar
            """,
            new { idContrato, pago = SituacaoPago, preservar },
            tx)).ToList();

        if (detalhes.Count == 0)
            return 0;

        var idsDetalhe = detalhes.Select(d => d.Id).Distinct().ToArray();
        var idsParcelas = detalhes.Select(d => d.IdParcelas).Distinct().ToArray();

        await conexao.ExecuteAsync(
            "DELETE FROM CONTAS_PAGAMENTO WHERE ID_CONTAS_DETALHES IN @idsDetalhe",
            new { idsDetalhe },
            tx);

        await conexao.ExecuteAsync(
            "DELETE FROM CONTAS_DETALHE WHERE ID IN @idsDetalhe",
            new { idsDetalhe },
            tx);

        await conexao.ExecuteAsync(
            """
            DELETE FROM CONTAS_PARCELAS
            WHERE ID IN @idsParcelas
              AND NOT EXISTS (
                  SELECT 1 FROM CONTAS_DETALHE d WHERE d.ID_CONTAS_PARCELAS = CONTAS_PARCELAS.ID
              )
            """,
            new { idsParcelas },
            tx);

        return idsDetalhe.Length;
    }

    private async Task<Dictionary<string, object?>> GerarParcelaAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int idContrato,
        int idPessoa,
        int? tipoPagamento,
        JsonElement parcela)
    {
        var contaId = ReadInt(parcela, "conta_id", "ID_ORIGINAL", "id_cobra");
        var idDetalhePayload = ReadInt(parcela, "id_click", "ID", "id_click_detalhe");
        var numero = ReadInt(parcela, "parcela", "NUMERO_PARCELA");
        if (numero <= 0)
            numero = 1;

        var statusCobra = ReadInt(parcela, "status_conta_id", "ID_SITUACAO_PARCELA");
        var canceladoPayload = (ReadString(parcela, "cancelado", "CANCELADO") ?? "N").ToUpperInvariant();
        var cancelar = canceladoPayload is "S" or "1"
            || statusCobra is 3 or 4;
        var preservar = ReadBool(parcela, "preservar");
        var situacao = statusCobra == SituacaoPago && !cancelar ? SituacaoPago : SituacaoAberto;

        if (cancelar)
            return ResultadoParcela(contaId, 0, "ignorado");

        var vencimento = ReadDate(parcela, "dt_vencimento", "DATA_VENCIMENTO") ?? DateTime.Today;
        var emissao = ReadDate(parcela, "dt_emissao", "DATA_LANCAMENTO") ?? vencimento;
        var pagamento = ReadDate(parcela, "dt_pagamento", "DATA_PAGAMENTO");
        var valor = ReadDecimal(parcela, "valor", "VALOR") ?? 0m;
        var valorPago = ReadDecimal(parcela, "valor_pago", "VALOR_PAGO") ?? 0m;
        var valorJuros = ReadDecimal(parcela, "valor_juros", "VALOR_JUROS") ?? 0m;
        var valorMulta = ReadDecimal(parcela, "valor_multa", "VALOR_MULTA") ?? 0m;
        var historico = ReadString(parcela, "obs", "HISTORICO_DETALHE", "HISTORICO") ?? "";
        var link = ReadString(parcela, "link_pagamento", "LINK_BOLETO");
        var idCobranca = ReadString(parcela, "id_cobranca", "NUM_COB_BOLETO", "NOSSO_NUMERO");

        var existente = await LocalizarDetalheAsync(
            conexao, tx, idContrato, contaId, idDetalhePayload, vencimento);

        if (existente is not null)
        {
            var idExistente = GetDynamicInt(existente, "ID");
            var situacaoExistente = GetDynamicInt(existente, "ID_SITUACAO_PARCELA");

            if (preservar || situacaoExistente == SituacaoPago)
            {
                await AplicarIntegradoNAsync(conexao, tx, colunas, "CONTAS_DETALHE", idExistente);
                var idParc = GetDynamicInt(existente, "ID_CONTAS_PARCELAS");
                if (idParc > 0)
                    await AplicarIntegradoNAsync(conexao, tx, colunas, "CONTAS_PARCELAS", idParc);

                return ResultadoParcela(
                    contaId,
                    idExistente,
                    preservar ? "preservado" : "pago");
            }
        }

        var idParcelas = await NextIdAsync(conexao, tx, "CONTAS_PARCELAS");
        var idDetalhe = await NextIdAsync(conexao, tx, "CONTAS_DETALHE");
        await InsertParcelasAsync(
            conexao, tx, colunas, idParcelas, idContrato, idPessoa, tipoPagamento,
            emissao, vencimento, historico, contaId, false);
        await InsertDetalheAsync(
            conexao, tx, colunas, idDetalhe, idParcelas, numero, situacao, vencimento,
            pagamento, valor, valorPago, valorJuros, valorMulta, historico, link,
            idCobranca, contaId, false);

        if (situacao == SituacaoPago)
            await GarantirPagamentoAsync(conexao, tx, colunas, idDetalhe, pagamento ?? DateTime.Today, valorPago, valorJuros, valorMulta, contaId);

        return ResultadoParcela(contaId, idDetalhe, "criado");
    }

    private static async Task<dynamic?> LocalizarDetalheAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        int idContrato,
        int contaId,
        int idDetalhePayload,
        DateTime vencimento)
    {
        if (contaId > 0)
        {
            var porOriginal = await conexao.QueryFirstOrDefaultAsync(
                """
                SELECT d.ID, d.ID_CONTAS_PARCELAS, d.ID_SITUACAO_PARCELA, d.CANCELADO
                FROM CONTAS_DETALHE d
                INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
                WHERE p.ID_CONTRATO = @idContrato AND d.ID_ORIGINAL = @contaId
                """,
                new { idContrato, contaId },
                tx);
            if (porOriginal is not null)
                return porOriginal;
        }

        if (idDetalhePayload > 0)
        {
            var porId = await conexao.QueryFirstOrDefaultAsync(
                """
                SELECT d.ID, d.ID_CONTAS_PARCELAS, d.ID_SITUACAO_PARCELA, d.CANCELADO
                FROM CONTAS_DETALHE d
                INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
                WHERE p.ID_CONTRATO = @idContrato AND d.ID = @idDetalhePayload
                """,
                new { idContrato, idDetalhePayload },
                tx);
            if (porId is not null)
                return porId;
        }

        return await conexao.QueryFirstOrDefaultAsync(
            """
            SELECT d.ID, d.ID_CONTAS_PARCELAS, d.ID_SITUACAO_PARCELA, d.CANCELADO
            FROM CONTAS_DETALHE d
            INNER JOIN CONTAS_PARCELAS p ON p.ID = d.ID_CONTAS_PARCELAS
            WHERE p.ID_CONTRATO = @idContrato
              AND ISNULL(d.CANCELADO, 'N') <> 'S'
              AND d.ID_SITUACAO_PARCELA = @aberto
              AND YEAR(d.DATA_VENCIMENTO) = @ano
              AND MONTH(d.DATA_VENCIMENTO) = @mes
            """,
            new
            {
                idContrato,
                aberto = SituacaoAberto,
                ano = vencimento.Year,
                mes = vencimento.Month
            },
            tx);
    }

    private async Task InsertParcelasAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int id,
        int idContrato,
        int idPessoa,
        int? tipoPagamento,
        DateTime emissao,
        DateTime vencimento,
        string historico,
        int contaId,
        bool cancelar)
    {
        var campos = new Dictionary<string, object?>
        {
            ["ID"] = id,
            ["ID_CONTRATO"] = idContrato,
            ["ID_PESSOA"] = idPessoa,
            ["TIPO"] = "R",
            ["DATA_LANCAMENTO"] = emissao.Date,
            ["PRIMEIRO_VENCIMENTO"] = vencimento.Date,
            ["HISTORICO"] = Trunc(historico, 255),
            ["CANCELADO"] = cancelar ? "S" : "N"
        };
        if (tipoPagamento is > 0)
            campos["ID_TIPO_PAGAMENTO"] = tipoPagamento.Value;
        if (contaId > 0)
            campos["ID_ORIGINAL"] = contaId;

        AdicionarIntegradoN(campos, colunas, "CONTAS_PARCELAS");
        await InsertAsync(conexao, tx, colunas, "CONTAS_PARCELAS", campos);
    }

    private async Task UpdateParcelasAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int id,
        DateTime emissao,
        DateTime vencimento,
        string historico,
        int contaId,
        bool cancelar)
    {
        var campos = new Dictionary<string, object?>
        {
            ["DATA_LANCAMENTO"] = emissao.Date,
            ["PRIMEIRO_VENCIMENTO"] = vencimento.Date,
            ["HISTORICO"] = Trunc(historico, 255),
            ["CANCELADO"] = cancelar ? "S" : "N"
        };
        if (contaId > 0)
            campos["ID_ORIGINAL"] = contaId;
        AdicionarIntegradoN(campos, colunas, "CONTAS_PARCELAS");
        await UpdateAsync(conexao, tx, colunas, "CONTAS_PARCELAS", id, campos);
    }

    private async Task InsertDetalheAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int id,
        int idParcelas,
        int numero,
        int situacao,
        DateTime vencimento,
        DateTime? pagamento,
        decimal valor,
        decimal valorPago,
        decimal valorJuros,
        decimal valorMulta,
        string historico,
        string? link,
        string? idCobranca,
        int contaId,
        bool cancelar)
    {
        var campos = new Dictionary<string, object?>
        {
            ["ID"] = id,
            ["ID_CONTAS_PARCELAS"] = idParcelas,
            ["NUMERO_PARCELA"] = numero,
            ["ID_SITUACAO_PARCELA"] = situacao,
            ["DATA_VENCIMENTO"] = vencimento.Date,
            ["DATA_PAGAMENTO"] = pagamento?.Date,
            ["VALOR"] = valor,
            ["VALOR_PAGO"] = valorPago,
            ["VALOR_JUROS"] = valorJuros,
            ["VALOR_MULTA"] = valorMulta,
            ["HISTORICO_DETALHE"] = Trunc(historico, 255),
            ["LINK_BOLETO"] = link,
            ["NUM_COB_BOLETO"] = Trunc(idCobranca, 80),
            ["CANCELADO"] = cancelar ? "S" : "N"
        };
        if (contaId > 0)
            campos["ID_ORIGINAL"] = contaId;
        AdicionarIntegradoN(campos, colunas, "CONTAS_DETALHE");
        await InsertAsync(conexao, tx, colunas, "CONTAS_DETALHE", campos);
    }

    private async Task UpdateDetalheAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int id,
        int numero,
        int situacao,
        DateTime vencimento,
        DateTime? pagamento,
        decimal valor,
        decimal valorPago,
        decimal valorJuros,
        decimal valorMulta,
        string historico,
        string? link,
        string? idCobranca,
        int contaId,
        bool cancelar)
    {
        var campos = new Dictionary<string, object?>
        {
            ["NUMERO_PARCELA"] = numero,
            ["ID_SITUACAO_PARCELA"] = situacao,
            ["DATA_VENCIMENTO"] = vencimento.Date,
            ["DATA_PAGAMENTO"] = pagamento?.Date,
            ["VALOR"] = valor,
            ["VALOR_PAGO"] = valorPago,
            ["VALOR_JUROS"] = valorJuros,
            ["VALOR_MULTA"] = valorMulta,
            ["HISTORICO_DETALHE"] = Trunc(historico, 255),
            ["LINK_BOLETO"] = link,
            ["NUM_COB_BOLETO"] = Trunc(idCobranca, 80),
            ["CANCELADO"] = cancelar ? "S" : "N"
        };
        if (contaId > 0)
            campos["ID_ORIGINAL"] = contaId;
        AdicionarIntegradoN(campos, colunas, "CONTAS_DETALHE");
        await UpdateAsync(conexao, tx, colunas, "CONTAS_DETALHE", id, campos);
    }

    private async Task GarantirPagamentoAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        int idDetalhe,
        DateTime dataPagamento,
        decimal valorPago,
        decimal valorJuros,
        decimal valorMulta,
        int contaId)
    {
        if (!colunas.Existe("CONTAS_PAGAMENTO", "ID"))
            return;

        var jaTem = await conexao.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM CONTAS_PAGAMENTO WHERE ID_CONTAS_DETALHES = @idDetalhe",
            new { idDetalhe },
            tx);
        if (jaTem > 0)
        {
            var upd = new Dictionary<string, object?>
            {
                ["DATA_PAGAMENTO"] = dataPagamento.Date,
                ["VALOR"] = valorPago
            };
            if (contaId > 0)
                upd["ID_ORIGINAL"] = contaId;
            AdicionarIntegradoN(upd, colunas, "CONTAS_PAGAMENTO");

            if (colunas.Existe("CONTAS_PAGAMENTO", "VALOR_JUROS"))
                upd["VALOR_JUROS"] = valorJuros;
            if (colunas.Existe("CONTAS_PAGAMENTO", "VALOR_MULTA"))
                upd["VALOR_MULTA"] = valorMulta;

            var sets = new List<string>();
            var param = new DynamicParameters();
            foreach (var kv in upd)
            {
                if (!colunas.Existe("CONTAS_PAGAMENTO", kv.Key))
                    continue;
                sets.Add($"{kv.Key} = @{kv.Key}");
                param.Add(kv.Key, kv.Value ?? (object)DBNull.Value);
            }

            if (sets.Count == 0)
                return;

            param.Add("idDetalhe", idDetalhe);
            await conexao.ExecuteAsync(
                $"UPDATE CONTAS_PAGAMENTO SET {string.Join(", ", sets)} WHERE ID_CONTAS_DETALHES = @idDetalhe",
                param,
                tx);
            return;
        }

        var idPag = await NextIdAsync(conexao, tx, "CONTAS_PAGAMENTO");
        var campos = new Dictionary<string, object?>
        {
            ["ID"] = idPag,
            ["ID_CONTAS_DETALHES"] = idDetalhe,
            ["DATA_PAGAMENTO"] = dataPagamento.Date,
            ["VALOR"] = valorPago
        };
        if (colunas.Existe("CONTAS_PAGAMENTO", "VALOR_JUROS"))
            campos["VALOR_JUROS"] = valorJuros;
        if (colunas.Existe("CONTAS_PAGAMENTO", "VALOR_MULTA"))
            campos["VALOR_MULTA"] = valorMulta;
        if (contaId > 0)
            campos["ID_ORIGINAL"] = contaId;
        AdicionarIntegradoN(campos, colunas, "CONTAS_PAGAMENTO");
        await InsertAsync(conexao, tx, colunas, "CONTAS_PAGAMENTO", campos);
    }

    private async Task AplicarIntegradoNAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        string tabela,
        int id)
    {
        var campos = new Dictionary<string, object?>();
        AdicionarIntegradoN(campos, colunas, tabela);
        if (campos.Count == 0)
            return;
        await UpdateAsync(conexao, tx, colunas, tabela, id, campos);
    }

    private static void AdicionarIntegradoN(
        Dictionary<string, object?> campos,
        ColumnCache colunas,
        string tabela)
    {
        if (!colunas.Existe(tabela, "INTEGRADO"))
            return;

        campos["INTEGRADO"] = colunas.EhBit(tabela, "INTEGRADO") ? 0 : "N";
    }

    private static async Task InsertAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        string tabela,
        Dictionary<string, object?> campos)
    {
        var validos = campos
            .Where(kv => colunas.Existe(tabela, kv.Key))
            .ToList();
        if (validos.Count == 0)
            return;

        var names = validos.Select(kv => kv.Key).ToList();
        var param = new DynamicParameters();
        foreach (var kv in validos)
            param.Add(kv.Key, kv.Value ?? (object)DBNull.Value);

        var sql = $"INSERT INTO {tabela} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select(n => "@" + n))})";
        await conexao.ExecuteAsync(sql, param, tx);
    }

    private static async Task UpdateAsync(
        SqlConnection conexao,
        SqlTransaction tx,
        ColumnCache colunas,
        string tabela,
        int id,
        Dictionary<string, object?> campos)
    {
        var sets = new List<string>();
        var param = new DynamicParameters();
        foreach (var kv in campos)
        {
            if (!colunas.Existe(tabela, kv.Key) || kv.Key.Equals("ID", StringComparison.OrdinalIgnoreCase))
                continue;
            sets.Add($"{kv.Key} = @{kv.Key}");
            param.Add(kv.Key, kv.Value ?? (object)DBNull.Value);
        }

        if (sets.Count == 0)
            return;

        param.Add("id", id);
        await conexao.ExecuteAsync(
            $"UPDATE {tabela} SET {string.Join(", ", sets)} WHERE ID = @id",
            param,
            tx);
    }

    private static async Task<int> NextIdAsync(SqlConnection conexao, SqlTransaction tx, string tabela)
    {
        return await conexao.ExecuteScalarAsync<int>(
            $"SELECT ISNULL(MAX(ID), 0) + 1 FROM {tabela} WITH (UPDLOCK, HOLDLOCK)",
            transaction: tx);
    }

    private static Dictionary<string, object?> ResultadoParcela(int contaId, int idClick, string acao) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["conta_id"] = contaId,
            ["id_click"] = idClick,
            ["acao"] = acao
        };

    private static int GetDynamicInt(object? row, string key)
    {
        var dict = JsonRowHelper.ToLowerDictionary(row);

        if (!dict.TryGetValue(key, out var value) || value is null)
            return 0;

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static List<JsonElement> ReadArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind != JsonValueKind.Array)
                continue;
            return el.EnumerateArray().ToList();
        }

        return [];
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

    private static bool ReadBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (el.ValueKind is JsonValueKind.True)
                return true;
            if (el.ValueKind is JsonValueKind.False)
                return false;
            var s = (el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString())?.Trim();
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                || s is "1" or "S" or "s")
                return true;
        }

        return false;
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

    private static string? Trunc(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
    }

    private sealed class ColumnCache
    {
        private readonly SqlConnection _conexao;
        private readonly Dictionary<string, Dictionary<string, string>> _tipos = new(StringComparer.OrdinalIgnoreCase);

        public ColumnCache(SqlConnection conexao) => _conexao = conexao;

        public async Task LoadAsync(params string[] tabelas)
        {
            foreach (var tabela in tabelas)
            {
                var rows = await _conexao.QueryAsync<(string COLUMN_NAME, string DATA_TYPE)>(
                    """
                    SELECT COLUMN_NAME, DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @tabela
                    """,
                    new { tabela });

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                    map[row.COLUMN_NAME] = row.DATA_TYPE;
                _tipos[tabela] = map;
            }
        }

        public bool Existe(string tabela, string coluna) =>
            _tipos.TryGetValue(tabela, out var map) && map.ContainsKey(coluna);

        public bool EhBit(string tabela, string coluna) =>
            _tipos.TryGetValue(tabela, out var map)
            && map.TryGetValue(coluna, out var tipo)
            && tipo.Equals("bit", StringComparison.OrdinalIgnoreCase);
    }
}
