using System.Data.SqlClient;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Options;
using ServidorHorseLsi.Api.Configuration;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface ICobraSaasReplicadorService
{
    /// <param name="inicial">
    /// true = carga completa (inclui contratos ja com ID_ORIGINAL).
    /// false = incremental (somente contratos ainda sem ID_ORIGINAL).
    /// </param>
    Task<Dictionary<string, object?>> ReplicarAsync(bool inicial = false, CancellationToken cancellationToken = default);
}

public sealed class CobraSaasReplicadorService : ICobraSaasReplicadorService
{
    /// <summary>CONTAS_DETALHE.ID_SITUACAO_PARCELA: 1 = aberto, 2 = pago.</summary>
    private const int SituacaoAberto = 1;
    private const int SituacaoPago = 2;
    private const int SqlTimeoutSegundos = 180;
    private readonly ISqlConnectionFactory _connections;
    private readonly ICobraSaasClient _cobra;
    private readonly CobraSaasOptions _options;
    private readonly ILogger<CobraSaasReplicadorService> _logger;

    public CobraSaasReplicadorService(
        ISqlConnectionFactory connections,
        ICobraSaasClient cobra,
        IOptions<CobraSaasOptions> options,
        ILogger<CobraSaasReplicadorService> logger)
    {
        _connections = connections;
        _cobra = cobra;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Dictionary<string, object?>> ReplicarAsync(
        bool inicial = false,
        CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var faltando = await ColunasObrigatoriasFaltandoAsync(conexao);
        if (faltando.Count > 0)
        {
            return JsonRowHelper.StatusObject(
                "2",
                "Execute Sql/migration-cobra-ids-sqlserver.sql. Colunas ausentes: " + string.Join(", ", faltando));
        }

        var anoAtual = DateTime.Today.Year;
        var contratoIds = (await conexao.QueryAsync<int>(
            Cmd(
                SqlContratosElegiveis,
                new
                {
                    anoAtual,
                    situacaoAberto = SituacaoAberto,
                    inicial = inicial ? 1 : 0
                }))).ToList();

        if (contratoIds.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "1",
                ["erro"] = inicial
                    ? "Nenhum contrato com parcela em aberto do ano atual em diante."
                    : "Nenhum contrato novo (sem ID_ORIGINAL) com parcela em aberto do ano atual em diante.",
                ["modo"] = inicial ? "inicial" : "incremental",
                ["contratos"] = 0,
                ["clientes"] = 0,
                ["parcelas"] = 0
            };
        }

        var contratos = (await conexao.QueryAsync(Cmd(SqlContratos, new { ids = contratoIds }))).ToList();
        if (contratos.Count == 0)
        {
            return JsonRowHelper.StatusObject(
                "2",
                "Parcelas elegiveis apontam para ID_CONTRATO inexistente em CONTRATOS. " +
                "Corrija o vinculo CONTAS_PARCELAS.ID_CONTRATO x CONTRATOS.ID antes de replicar.");
        }

        var idsContratosExistentes = contratos
            .Select(r => GetInt(r, "ID"))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var clienteIds = contratos
            .Select(r => GetInt(r, "ID_CLIENTE"))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var tipoContratoIds = contratos
            .Select(r => GetInt(r, "ID_TIPO_CONTRATO"))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var clientes = clienteIds.Count == 0
            ? []
            : (await conexao.QueryAsync(Cmd(SqlClientes, new { ids = clienteIds }))).ToList();

        var temIdOriginalPlano = await ColunaExisteAsync(conexao, "TIPO_CONTRATO", "ID_ORIGINAL");

        var planos = tipoContratoIds.Count == 0
            ? []
            : (await conexao.QueryAsync(
                Cmd(
                    temIdOriginalPlano ? SqlTiposContratoComOriginal : SqlTiposContrato,
                    new { ids = tipoContratoIds }))).ToList();

        var parcelas = (await conexao.QueryAsync(Cmd(SqlParcelas, new
            {
                ids = idsContratosExistentes,
                anoAtual,
                situacaoAberto = SituacaoAberto,
                situacaoPago = SituacaoPago,
                inicial = inicial ? 1 : 0
            }))).ToList();

        var payloadPlanos = planos.Select(MapPlano).ToList();
        var payloadClientes = clientes.Select(MapCliente).ToList();
        var payloadContratos = contratos.Select(MapContrato).ToList();
        var payloadContas = parcelas.Select(MapConta).ToList();

        // Ordem: planos → clientes → contratos → parcelas
        var resumoPlanos = await EnviarEmLotesAsync(payloadPlanos, _cobra.EnviarPlanosAsync, cancellationToken);
        if (temIdOriginalPlano)
            await GravarIdsPlanoAsync(conexao, resumoPlanos.Registros);

        var resumoClientes = await EnviarEmLotesAsync(payloadClientes, _cobra.EnviarClientesAsync, cancellationToken);
        await GravarIdsClienteAsync(conexao, resumoClientes.Registros);

        var resumoContratos = await EnviarEmLotesAsync(payloadContratos, _cobra.EnviarContratosAsync, cancellationToken);
        await GravarIdsContratoAsync(conexao, resumoContratos.Registros);

        // Envia parcelas so dos contratos aceitos no Cobra.
        var contratosOk = resumoContratos.Registros
            .Select(r => ReadMapInt(r, "id_click"))
            .Where(id => id > 0)
            .ToHashSet();

        CobraLoteResultado resumoContas;
        if (contratosOk.Count == 0)
        {
            resumoContas = new CobraLoteResultado
            {
                Sucesso = false,
                Erro = "Parcelas nao enviadas: nenhum contrato foi gravado no Cobra.",
                Total = payloadContas.Count,
                Erros = payloadContas.Count,
                Falhas = [],
                Registros = []
            };
        }
        else
        {
            var contasOk = payloadContas
                .Where(c => contratosOk.Contains(Convert.ToInt32(c.GetValueOrDefault("id_click_contrato") ?? 0, CultureInfo.InvariantCulture)))
                .ToList();
            resumoContas = await EnviarEmLotesAsync(contasOk, _cobra.EnviarContasAsync, cancellationToken);
            await GravarIdsContaAsync(conexao, resumoContas.Registros);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = Falhou(resumoPlanos, resumoClientes, resumoContratos, resumoContas) ? "2" : "1",
            ["erro"] = Falhou(resumoPlanos, resumoClientes, resumoContratos, resumoContas)
                ? "Alguns registros falharam. Veja planos/clientes/contratos/parcelas."
                : "Replicacao concluida.",
            ["modo"] = inicial ? "inicial" : "incremental",
            ["ano"] = anoAtual,
            ["planos"] = ResumoLote(resumoPlanos),
            ["clientes"] = ResumoLote(resumoClientes),
            ["contratos"] = ResumoLote(resumoContratos),
            ["parcelas"] = ResumoLote(resumoContas)
        };
    }

    private async Task<CobraLoteResultado> EnviarEmLotesAsync(
        List<Dictionary<string, object?>> itens,
        Func<IReadOnlyList<Dictionary<string, object?>>, CancellationToken, Task<CobraLoteResultado>> enviar,
        CancellationToken cancellationToken)
    {
        var agregado = new CobraLoteResultado
        {
            Sucesso = true,
            Falhas = [],
            Registros = []
        };

        if (itens.Count == 0)
            return agregado;

        var batchSize = Math.Max(1, _options.BatchSize);
        for (var i = 0; i < itens.Count; i += batchSize)
        {
            var lote = itens.Skip(i).Take(batchSize).ToList();
            var resultado = await enviar(lote, cancellationToken);
            agregado = Somar(agregado, resultado);
            if (!resultado.Sucesso)
                _logger.LogWarning("Falha ao enviar lote Cobra: {Erro}", resultado.Erro);
        }

        return agregado;
    }

    private static CobraLoteResultado Somar(CobraLoteResultado a, CobraLoteResultado b)
    {
        var falhas = a.Falhas.Concat(b.Falhas).ToList();
        var registros = a.Registros.Concat(b.Registros).ToList();
        return new CobraLoteResultado
        {
            Sucesso = a.Sucesso && b.Sucesso,
            Erro = a.Erro ?? b.Erro,
            Total = a.Total + b.Total,
            Processados = a.Processados + b.Processados,
            Criados = a.Criados + b.Criados,
            Atualizados = a.Atualizados + b.Atualizados,
            Erros = a.Erros + b.Erros,
            Falhas = falhas,
            Registros = registros
        };
    }

    private static bool Falhou(params CobraLoteResultado[] lotes) =>
        lotes.Any(l => !l.Sucesso || l.Erros > 0);

    private static Dictionary<string, object?> ResumoLote(CobraLoteResultado lote) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["sucesso"] = lote.Sucesso,
            ["erro"] = lote.Erro,
            ["total"] = lote.Total,
            ["processados"] = lote.Processados,
            ["criados"] = lote.Criados,
            ["atualizados"] = lote.Atualizados,
            ["erros"] = lote.Erros,
            ["falhas"] = lote.Falhas
        };

    private static Dictionary<string, object?> MapPlano(dynamic row)
    {
        var idOriginal = GetInt(row, "ID_ORIGINAL");
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id_click"] = GetInt(row, "ID"),
            ["plano_contrato_id"] = idOriginal > 0 ? idOriginal : null,
            ["DESCRICAO"] = GetString(row, "DESCRICAO"),
            ["nome"] = GetString(row, "DESCRICAO"),
            ["VALOR"] = GetValue(row, "VALOR"),
            ["VALOR_ADICIONAL"] = GetValue(row, "VALOR_ADICIONAL"),
            ["QTD_USUARIOS"] = GetValue(row, "QTD_USUARIOS"),
            ["QTD_PDV"] = GetValue(row, "QTD_PDV"),
            ["QTD_BALCAO"] = GetValue(row, "QTD_BALCAO"),
            ["QTD_MESES"] = GetValue(row, "QTD_MESES")
        };
    }

    private static Dictionary<string, object?> MapCliente(dynamic row)
    {
        var d = ToDict(row);
        d["id_click"] = GetInt(row, "ID");
        var pessoaId = ReadOriginalInt(GetString(row, "ID_ORIGINAL_STRING"));
        if (pessoaId > 0)
            d["pessoa_id"] = pessoaId;
        return d;
    }

    private static Dictionary<string, object?> MapContrato(dynamic row)
    {
        // ID_TIPO_CONTRATO → plano_contrato.id_click (apos receberPlanos).
        var idOriginal = GetInt(row, "ID_ORIGINAL");
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id_click"] = GetInt(row, "ID"),
            ["contrato_cliente_id"] = idOriginal > 0 ? idOriginal : null,
            ["id_click_cliente"] = GetInt(row, "ID_CLIENTE"),
            ["ID_TIPO_CONTRATO"] = GetInt(row, "ID_TIPO_CONTRATO"),
            ["id_click_plano"] = GetInt(row, "ID_TIPO_CONTRATO"),
            ["ID_STATUS_CONTRATO"] = GetInt(row, "ID_STATUS_CONTRATO"),
            ["ID_LOCAL_INSTALACAO"] = GetInt(row, "ID_LOCAL_INSTALACAO"),
            ["LOCAL_INSTALACAO"] = GetString(row, "LOCAL_INSTALACAO"),
            ["descricao_instalacao"] = GetString(row, "LOCAL_INSTALACAO"),
            ["DATA_CADASTRO"] = FormatDate(GetValue(row, "DATA_CADASTRO")),
            ["PRIMEIRO_VENCIMENTO"] = FormatDate(GetValue(row, "PRIMEIRO_VENCIMENTO")),
            ["DATA_VALIDADE"] = FormatDate(GetValue(row, "DATA_VALIDADE")),
            ["QTDE_MESES"] = GetValue(row, "QTDE_MESES"),
            ["QTDE_USUARIOS"] = GetValue(row, "QTDE_USUARIOS"),
            ["QTDE_PDV"] = GetValue(row, "QTDE_PDV"),
            ["QTDE_BALCAO"] = GetValue(row, "QTDE_BALCAO"),
            ["QTD_USUARIOS_AD"] = GetValue(row, "QTD_USUARIOS_AD"),
            ["QTD_PDV_AD"] = GetValue(row, "QTD_PDV_AD"),
            ["QTD_BALCAO_AD"] = GetValue(row, "QTD_BALCAO_AD"),
            ["VALOR_CONTRATO"] = GetValue(row, "VALOR_CONTRATO"),
            ["valor_receber"] = GetValue(row, "valor_receber"),
            ["VALOR_LICENCA"] = GetValue(row, "VALOR_LICENCA"),
            ["ATIVO"] = GetString(row, "ATIVO"),
            ["contrato_assinado"] = GetString(row, "contrato_assinado"),
            ["OBS"] = GetString(row, "OBS")
        };
    }

    private static Dictionary<string, object?> MapConta(dynamic row)
    {
        var d = ToDict(row);
        var situacao = GetInt(row, "ID_SITUACAO_PARCELA");
        var idOriginal = GetInt(row, "ID_ORIGINAL");
        d["id_click"] = GetInt(row, "ID");
        d["conta_id"] = idOriginal > 0 ? idOriginal : null;
        d["id_click_contrato"] = GetInt(row, "ID_CONTRATO");
        d["id_click_cliente"] = GetInt(row, "ID_PESSOA");
        d["tipo"] = GetString(row, "TIPO");
        // CONTAS_DETALHE.ID_SITUACAO_PARCELA: 1 aberto, 2 pago (= status_conta_id no Cobra)
        d["status_conta_id"] = situacao is SituacaoAberto or SituacaoPago ? situacao : SituacaoAberto;
        d["dt_vencimento"] = FormatDate(GetValue(row, "DATA_VENCIMENTO"));
        d["dt_emissao"] = FormatDate(GetValue(row, "DATA_LANCAMENTO"));
        d["dt_pagamento"] = FormatDate(GetValue(row, "DATA_PAGAMENTO"));
        d["valor"] = GetValue(row, "VALOR");
        d["valor_pago"] = GetValue(row, "VALOR_PAGO");
        d["valor_juros"] = GetValue(row, "VALOR_JUROS");
        d["valor_multa"] = GetValue(row, "VALOR_MULTA");
        d["parcela"] = GetValue(row, "NUMERO_PARCELA");
        d["obs"] = GetValue(row, "HISTORICO_DETALHE") ?? GetValue(row, "HISTORICO");
        d["link_pagamento"] = GetValue(row, "LINK_BOLETO");
        d["id_cobranca"] = GetValue(row, "NUM_COB_BOLETO") ?? GetValue(row, "NOSSO_NUMERO");
        return d;
    }

    private static async Task GravarIdsPlanoAsync(SqlConnection conexao, IReadOnlyList<Dictionary<string, object?>> registros)
    {
        foreach (var item in registros)
        {
            var idClick = ReadMapInt(item, "id_click");
            var planoCobra = ReadMapInt(item, "plano_contrato_id");
            if (idClick <= 0 || planoCobra <= 0)
                continue;

            await conexao.ExecuteAsync(
                Cmd("UPDATE TIPO_CONTRATO SET ID_ORIGINAL = @planoCobra WHERE ID = @idClick",
                    new { planoCobra, idClick }));
        }
    }

    private static async Task GravarIdsClienteAsync(SqlConnection conexao, IReadOnlyList<Dictionary<string, object?>> registros)
    {
        foreach (var item in registros)
        {
            var idClick = ReadMapInt(item, "id_click");
            var pessoaId = ReadMapInt(item, "pessoa_id");
            if (idClick <= 0 || pessoaId <= 0)
                continue;

            await conexao.ExecuteAsync(
                Cmd("UPDATE CLIENTE SET ID_ORIGINAL_STRING = @pessoaId WHERE ID = @idClick",
                    new { pessoaId = pessoaId.ToString(CultureInfo.InvariantCulture), idClick }));
        }
    }

    private static async Task GravarIdsContratoAsync(SqlConnection conexao, IReadOnlyList<Dictionary<string, object?>> registros)
    {
        foreach (var item in registros)
        {
            var idClick = ReadMapInt(item, "id_click");
            var contratoCobra = ReadMapInt(item, "contrato_cliente_id");
            if (idClick <= 0 || contratoCobra <= 0)
                continue;

            await conexao.ExecuteAsync(
                Cmd("UPDATE CONTRATOS SET ID_ORIGINAL = @contratoCobra WHERE ID = @idClick",
                    new { contratoCobra, idClick }));
        }
    }

    /// <summary>
    /// Cobra SaaS tem uma linha em conta por parcela. No Click, grava conta.id em ID_ORIGINAL
    /// nas tres tabelas ligadas: PARCELAS (cabecalho) → DETALHE (parcela) → PAGAMENTO (baixa).
    /// id_click = CONTAS_DETALHE.ID enviado na replicacao.
    /// </summary>
    private static CommandDefinition Cmd(string sql, object? param = null) =>
        new(sql, param, commandTimeout: SqlTimeoutSegundos, cancellationToken: CancellationToken.None);

    private static async Task GravarIdsContaAsync(SqlConnection conexao, IReadOnlyList<Dictionary<string, object?>> registros)
    {
        var pares = registros
            .Select(item => (idDetalhe: ReadMapInt(item, "id_click"), contaCobraId: ReadMapInt(item, "conta_id")))
            .Where(p => p.idDetalhe > 0 && p.contaCobraId > 0)
            .ToList();

        const int lote = 200;
        for (var i = 0; i < pares.Count; i += lote)
        {
            var chunk = pares.Skip(i).Take(lote).ToList();
            var valores = string.Join(",", chunk.Select(p => $"({p.idDetalhe},{p.contaCobraId})"));
            await conexao.ExecuteAsync(
                Cmd(
                    $"""
                    IF OBJECT_ID('tempdb..#map_conta') IS NOT NULL DROP TABLE #map_conta;
                    CREATE TABLE #map_conta (idDetalhe int, contaCobraId int);
                    INSERT INTO #map_conta (idDetalhe, contaCobraId) VALUES {valores};

                    UPDATE d SET d.ID_ORIGINAL = m.contaCobraId
                    FROM CONTAS_DETALHE d
                    INNER JOIN #map_conta m ON m.idDetalhe = d.ID;

                    UPDATE p SET p.ID_ORIGINAL = m.contaCobraId
                    FROM CONTAS_PARCELAS p
                    INNER JOIN CONTAS_DETALHE d ON d.ID_CONTAS_PARCELAS = p.ID
                    INNER JOIN #map_conta m ON m.idDetalhe = d.ID;

                    UPDATE pg SET pg.ID_ORIGINAL = m.contaCobraId
                    FROM CONTAS_PAGAMENTO pg
                    INNER JOIN #map_conta m ON m.idDetalhe = pg.ID_CONTAS_DETALHES;

                    DROP TABLE #map_conta;
                    """));
        }
    }

    private static async Task<List<string>> ColunasObrigatoriasFaltandoAsync(SqlConnection conexao)
    {
        var exigidas = new (string Tabela, string Coluna)[]
        {
            ("CLIENTE", "ID_ORIGINAL_STRING"),
            ("CONTRATOS", "ID_ORIGINAL"),
            ("CONTAS_PARCELAS", "ID_ORIGINAL"),
            ("CONTAS_DETALHE", "ID_ORIGINAL"),
            ("CONTAS_PAGAMENTO", "ID_ORIGINAL")
        };

        var faltando = new List<string>();
        foreach (var (tabela, coluna) in exigidas)
        {
            var existe = await conexao.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @tabela AND COLUMN_NAME = @coluna
                """,
                new { tabela, coluna });

            if (existe == 0)
                faltando.Add($"{tabela}.{coluna}");
        }

        return faltando;
    }

    private static async Task<bool> ColunaExisteAsync(SqlConnection conexao, string tabela, string coluna)
    {
        var existe = await conexao.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @tabela AND COLUMN_NAME = @coluna
            """,
            new { tabela, coluna });

        return existe > 0;
    }

    private static Dictionary<string, object?> ToDict(dynamic row)
    {
        var dict = JsonRowHelper.ToLowerDictionary((object?)row);
        foreach (var key in dict.Keys.ToList())
        {
            if (dict[key] is string iso && iso.Contains('T', StringComparison.Ordinal) && DateTime.TryParse(iso, out var dt))
                dict[key] = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return dict;
    }

    private static object? GetValue(dynamic row, string name)
    {
        if (row is IDictionary<string, object> dapper)
        {
            foreach (var kv in dapper)
            {
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value is DBNull ? null : kv.Value;
            }
        }

        return null;
    }

    private static int GetInt(dynamic row, string name)
    {
        var value = GetValue(row, name);
        return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string? GetString(dynamic row, string name) => GetValue(row, name)?.ToString();

    private static string? FormatDate(object? value)
    {
        if (value is null or DBNull)
            return null;
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (DateTime.TryParse(value.ToString(), out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static int ReadOriginalInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static int ReadMapInt(Dictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
            return 0;
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private const string SqlContratosElegiveis = """
        SELECT DISTINCT p.ID_CONTRATO
        FROM CONTAS_PARCELAS p WITH (NOLOCK)
        INNER JOIN CONTAS_DETALHE d WITH (NOLOCK) ON d.ID_CONTAS_PARCELAS = p.ID
        INNER JOIN CONTRATOS c WITH (NOLOCK) ON c.ID = p.ID_CONTRATO
        WHERE p.ID_CONTRATO IS NOT NULL
          AND ISNULL(p.CANCELADO, 'N') <> 'S'
          AND ISNULL(d.CANCELADO, 'N') <> 'S'
          AND d.ID_SITUACAO_PARCELA = @situacaoAberto
          AND YEAR(ISNULL(d.DATA_VENCIMENTO, p.PRIMEIRO_VENCIMENTO)) >= @anoAtual
          AND (@inicial = 1 OR c.ID_ORIGINAL IS NULL)
        """;

    private const string SqlTiposContrato = """
        SELECT
            ID, DESCRICAO, VALOR, VALOR_ADICIONAL, ID_PRODUTO,
            VALOR_NFE, VALOR_BALCAO, QTD_MESES, QTD_BALCAO, QTD_USUARIOS, QTD_PDV, VALOR_TEF
        FROM TIPO_CONTRATO
        WHERE ID IN @ids
        """;

    private const string SqlTiposContratoComOriginal = """
        SELECT
            ID, ID_ORIGINAL, DESCRICAO, VALOR, VALOR_ADICIONAL, ID_PRODUTO,
            VALOR_NFE, VALOR_BALCAO, QTD_MESES, QTD_BALCAO, QTD_USUARIOS, QTD_PDV, VALOR_TEF
        FROM TIPO_CONTRATO
        WHERE ID IN @ids
        """;

    private const string SqlContratos = """
        SELECT
            ID, ID_ORIGINAL, ID_TIPO_CONTRATO, ID_CLIENTE, ID_STATUS_CONTRATO, DATA_CADASTRO, DATA_VALIDADE,
            QTDE_MESES, VALOR_CONTRATO, OBS, PRIMEIRO_VENCIMENTO, ATIVO,
            QTDE_PDV, QTDE_USUARIOS, QTDE_BALCAO,
            QTD_BALCAO_AD, QTD_USUARIOS_AD, QTD_PDV_AD,
            VALOR_LICENCA, valor_receber, ID_LOCAL_INSTALACAO, LOCAL_INSTALACAO, contrato_assinado
        FROM CONTRATOS
        WHERE ID IN @ids
        """;

    private const string SqlClientes = """
        SELECT
            ID, ID_ORIGINAL_STRING, NOME, FANTASIA, TIPO_PESSOA, CPF_CNPJ, CEP, LOGRADOURO, NUMERO, COMPLEMENTO,
            BAIRRO, CIDADE, UF, PONTO_REFERENCIA_ENDERECO, FONE1, FONE2, CONTATO,
            EMAIL, EMAIL_FINANCEIRO, CONTATO_FINANCEIRO, CPF_REPLEGAL, RESP_LEGAL,
            DATA_NASCIMENTO, OBSERVACAO
        FROM CLIENTE
        WHERE ID IN @ids
        """;

    private const string SqlParcelas = """
        SELECT
            d.ID,
            d.ID_ORIGINAL,
            p.ID_CONTRATO,
            p.ID_PESSOA,
            p.TIPO,
            p.ID_TIPO_PAGAMENTO,
            d.ID_SITUACAO_PARCELA,
            d.NUMERO_PARCELA,
            d.DATA_VENCIMENTO,
            p.DATA_LANCAMENTO,
            d.DATA_PAGAMENTO,
            d.VALOR,
            d.VALOR_PAGO,
            d.VALOR_JUROS,
            d.VALOR_MULTA,
            d.HISTORICO_DETALHE,
            p.HISTORICO,
            d.LINK_BOLETO,
            d.NUM_COB_BOLETO,
            d.NOSSO_NUMERO,
            d.CANCELADO
        FROM CONTAS_DETALHE d WITH (NOLOCK)
        INNER JOIN CONTAS_PARCELAS p WITH (NOLOCK) ON p.ID = d.ID_CONTAS_PARCELAS
        WHERE p.ID_CONTRATO IN @ids
          AND ISNULL(p.CANCELADO, 'N') <> 'S'
          AND ISNULL(d.CANCELADO, 'N') <> 'S'
          AND YEAR(ISNULL(d.DATA_VENCIMENTO, p.PRIMEIRO_VENCIMENTO)) >= @anoAtual
          AND (@inicial = 1 OR d.ID_ORIGINAL IS NULL)
          AND (
                d.ID_SITUACAO_PARCELA = @situacaoAberto
                OR (
                    YEAR(d.DATA_VENCIMENTO) = @anoAtual
                    AND d.ID_SITUACAO_PARCELA = @situacaoPago
                    AND EXISTS (
                        SELECT 1
                        FROM CONTAS_DETALHE d2
                        INNER JOIN CONTAS_PARCELAS p2 ON p2.ID = d2.ID_CONTAS_PARCELAS
                        WHERE p2.ID_CONTRATO = p.ID_CONTRATO
                          AND ISNULL(d2.CANCELADO, 'N') <> 'S'
                          AND d2.ID_SITUACAO_PARCELA = @situacaoAberto
                          AND YEAR(d2.DATA_VENCIMENTO) > @anoAtual
                    )
                )
          )
        ORDER BY p.ID_CONTRATO, d.NUMERO_PARCELA, d.ID
        """;
}
