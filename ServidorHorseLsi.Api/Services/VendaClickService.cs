using System.Data.SqlClient;
using System.Globalization;
using System.Text.Json;
using Dapper;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IVendaClickService
{
    Task<Dictionary<string, object?>> RenovarClickfreeAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Após pagamento Click Free: grava PEDIDO_CABECALHO / PEDIDO_DETALHE / PEDIDO_VEND_TIPO_PAGAMENTO no SQL.
/// Não chama rest.php do gestor.
/// </summary>
public sealed class VendaClickService : IVendaClickService
{
    private const int ProdutoRenovacaoClickFree = 2;
    private const int ProdutoSuporteAvulso = 3;
    private const int SqlTimeoutSegundos = 90;

    private readonly ISqlConnectionFactory _connections;
    private readonly ILogger<VendaClickService> _logger;

    public VendaClickService(
        ISqlConnectionFactory connections,
        ILogger<VendaClickService> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task<Dictionary<string, object?>> RenovarClickfreeAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
            return JsonRowHelper.StatusObject("2", "Body JSON obrigatorio.");

        var root = body.RootElement;
        if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            root = dataEl;

        var cnpj = DigitsOnly(ReadString(root, "cnpj", "CPF_CNPJ", "cpf_cnpj"));
        if (cnpj.Length is not (11 or 14))
            return JsonRowHelper.StatusObject("2", "CNPJ/CPF obrigatorio para renovar ClickFree.");

        var clickfree = LerOrigemClickfree(root);
        var origem = clickfree ? "clickfree" : "click";
        var valor = ReadDecimal(root, "valor", "VALOR") ?? 0m;
        var descricao = ReadString(root, "descricao", "DESCRICAO", "nome_produto")
            ?? (clickfree ? "Click Free" : "Click");
        var nomeCliente = ReadString(root, "nome_cliente", "razao_social", "nome") ?? "";
        var idServicoCobra = (int)(ReadDecimal(root, "id_servico") ?? 0);
        var idProduto = (int)(ReadDecimal(root, "id_produto") ?? 0);
        if (idProduto <= 0)
            idProduto = idServicoCobra > 0 ? idServicoCobra : (clickfree ? ProdutoRenovacaoClickFree : 0);
        var idPedidoApp = (int)(ReadDecimal(root, "cobranca_avulsa_id", "id_pedido_app") ?? 0);
        var billingType = (ReadString(root, "billing_type", "billingType", "forma") ?? "PIX").ToUpperInvariant();
        var idLocal = (int)(ReadDecimal(root, "id_local_instalacao") ?? 1);
        var idLicenca = (int)(ReadDecimal(root, "id_licenca", "id_licenca_free") ?? 0);

        if (idLocal <= 0)
            idLocal = 1;

        if (!clickfree && idProduto <= 0)
            return JsonRowHelper.StatusObject("2", "id_produto obrigatorio para pedido Click.");

        var ehSuporteAvulso = idServicoCobra == ProdutoSuporteAvulso || idProduto == ProdutoSuporteAvulso;

        Dictionary<string, object?> pedido;
        Dictionary<string, object?>? licenca = null;
        try
        {
            await using var conexao = await AbrirConexaoPedidoAsync(clickfree, CancellationToken.None);
            pedido = await InserirPedidoCabecalhoAsync(
                conexao, cnpj, nomeCliente, valor, descricao, idProduto, idPedidoApp, billingType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao inserir pedido {Origem} para {Cnpj}", origem, cnpj);
            pedido = JsonRowHelper.StatusObject("2", "Falha ao inserir pedido no Click: " + ex.Message);
        }

        if (clickfree && !ehSuporteAvulso)
        {
            try
            {
                await using var conexaoFree = _connections.CreateGestorFree();
                await conexaoFree.OpenWithLabelAsync("GestorFree", "GESTOR_FREE", CancellationToken.None);
                licenca = await RenovarLicencaFreeAsync(conexaoFree, cnpj, idLicenca, idLocal);
            }
            catch (Exception exLicenca)
            {
                _logger.LogError(exLicenca, "Falha ao renovar licenca Click Free para {Cnpj}", cnpj);
                licenca = JsonRowHelper.StatusObject("2", "Falha ao renovar licenca: " + exLicenca.Message);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "1",
            ["erro"] = "Pedido gravado no SQL.",
            ["cnpj"] = cnpj,
            ["origem"] = origem,
            ["banco"] = "GESTOR",
            ["pedido"] = pedido,
            ["licenca"] = licenca
        };
    }

    private async Task<SqlConnection> AbrirConexaoPedidoAsync(bool clickfree, CancellationToken cancellationToken)
    {
        var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);
        return conexao;
    }

    private static bool LerOrigemClickfree(JsonElement root)
    {
        var origem = ReadString(root, "origem", "sistema", "banco");
        if (!string.IsNullOrEmpty(origem))
        {
            origem = origem.Trim();
            if (origem.Equals("clickfree", StringComparison.OrdinalIgnoreCase)
                || origem.Equals("free", StringComparison.OrdinalIgnoreCase)
                || origem.Equals("gestor_free", StringComparison.OrdinalIgnoreCase))
                return true;

            if (origem.Equals("click", StringComparison.OrdinalIgnoreCase)
                || origem.Equals("gestor", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (TryGetProperty(root, "clickfree", out var flag))
        {
            if (flag.ValueKind == JsonValueKind.True)
                return true;
            if (flag.ValueKind == JsonValueKind.False)
                return false;
            if (flag.ValueKind == JsonValueKind.Number && flag.TryGetInt32(out var n))
                return n == 1;
            var texto = flag.ValueKind == JsonValueKind.String ? flag.GetString() : flag.ToString();
            if (!string.IsNullOrWhiteSpace(texto)
                && (texto.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || texto.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || texto.Equals("clickfree", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private async Task<Dictionary<string, object?>> RenovarLicencaFreeAsync(
        SqlConnection conexao,
        string cnpj,
        int idLicenca,
        int idLocal)
    {
            var consulta = await conexao.QueryFirstOrDefaultAsync<dynamic>(
                Cmd(
                    $"""
                    SELECT TOP 1 *
                    FROM vw_api_cliente_tem_licenca WITH (NOLOCK)
                    WHERE {CpfCnpjLookup.ViewWhere}
                    """,
                    CpfCnpjLookup.Args(cnpj)));

        var idView = ReadIdFromRow(consulta, "id", "id_licenca");
        var idSac = ReadIdFromRow(consulta, "id_sac_licenca", "id_original");

        if (idLicenca <= 0)
            idLicenca = idView;

        if (idLicenca <= 0)
            return JsonRowHelper.StatusObject("2", "Licenca Click Free nao encontrada para renovar.");

        var ids = new[] { idLicenca, idView, idSac }.Where(id => id > 0).Distinct().ToArray();
        var atualizados = 0;
        var alvos = await TabelasLicencaAtualizaveisAsync(conexao);

        foreach (var id in ids)
        {
            foreach (var (tabela, colunaStatus) in alvos)
                atualizados += await AtualizarValidadeStatusAsync(conexao, tabela, colunaStatus, id);
        }

        var renovada = await conexao.QueryFirstOrDefaultAsync<dynamic>(
            Cmd(
                $"""
                SELECT TOP 1 *
                FROM vw_api_cliente_tem_licenca WITH (NOLOCK)
                WHERE {CpfCnpjLookup.ViewWhere}
                   OR id = @id
                """,
                new { digits = cnpj, masked = CpfCnpjLookup.Mask(cnpj), id = idLicenca }))
            ?? await conexao.QueryFirstOrDefaultAsync<dynamic>(
                Cmd("SELECT * FROM licenca WITH (NOLOCK) WHERE id = @id", new { id = idLicenca }));

        _logger.LogInformation(
            "Licenca Click Free renovada id={Id} local={Local} cnpj={Cnpj} linhas={Linhas}",
            idLicenca, idLocal, cnpj, atualizados);

        return JsonRowHelper.MergeRow((object?)renovada,
            ("status", atualizados > 0 ? "1" : "2"),
            ("erro", atualizados > 0 ? "Licenca renovada com sucesso." : "Nenhuma licenca atualizada."),
            ("id", idLicenca),
            ("id_local_instalacao", idLocal));
    }

    private static async Task<List<(string Tabela, string ColunaStatus)>> TabelasLicencaAtualizaveisAsync(SqlConnection conexao)
    {
        var candidatas = new[]
        {
            ("sac_licenca", "id_sac_status_licenca"),
            ("licenca", "id_status_licenca"),
        };
        var alvos = new List<(string, string)>();

        foreach (var (tabela, coluna) in candidatas)
        {
            var ok = await conexao.ExecuteScalarAsync<int>(
                Cmd(
                    """
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE LOWER(TABLE_NAME) = LOWER(@tabela)
                      AND LOWER(COLUMN_NAME) IN (LOWER(@coluna), 'data_validade')
                    """,
                    new { tabela, coluna }));

            if (ok >= 2)
                alvos.Add((tabela, coluna));
        }

        return alvos;
    }

    private async Task<int> AtualizarValidadeStatusAsync(
        SqlConnection conexao,
        string tabela,
        string colunaStatus,
        int id)
    {
        try
        {
            return await conexao.ExecuteAsync(
                Cmd(
                    $"""
                    UPDATE {tabela}
                    SET data_validade = DATEADD(YEAR, 1, GETDATE()),
                        {colunaStatus} = 6
                    WHERE id = @id
                    """,
                    new { id }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao atualizou {Tabela}.{Coluna} id={Id}", tabela, colunaStatus, id);
            return 0;
        }
    }

    private static int ReadIdFromRow(object? row, params string[] names)
    {
        var dict = JsonRowHelper.ToLowerDictionary(row);
        foreach (var name in names)
        {
            if (!dict.TryGetValue(name, out var value) || value is null)
                continue;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var id)
                && id > 0)
                return id;
        }

        return 0;
    }

    private async Task<Dictionary<string, object?>> InserirPedidoCabecalhoAsync(
        SqlConnection conexao,
        string cnpj,
        string nomeCliente,
        decimal valor,
        string descricao,
        int idProduto,
        int idPedidoApp,
        string billingType)
    {
        if (valor <= 0)
            return JsonRowHelper.StatusObject("2", "Valor do pedido invalido.");

        if (idPedidoApp > 0)
        {
            int? existente = null;
            try
            {
                existente = await conexao.ExecuteScalarAsync<int?>(
                    Cmd(
                        "SELECT TOP 1 ID FROM PEDIDO_CABECALHO WITH (NOLOCK) WHERE ID_PEDIDO_APP = @idPedidoApp",
                        new { idPedidoApp }));
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                _logger.LogWarning(
                    "Timeout ao checar ID_PEDIDO_APP={Id}. Seguindo com insert. Crie o indice IX_PEDIDO_CABECALHO_ID_PEDIDO_APP.",
                    idPedidoApp);
            }

            if (existente is > 0)
            {
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = "1",
                    ["erro"] = "Pedido ja gravado no SQL.",
                    ["id"] = existente.Value
                };
            }
        }

        var cliente = await ObterOuCriarClienteAsync(conexao, cnpj, nomeCliente);
        var dataEmissao = DateTime.Today;
        var nomeProduto = string.IsNullOrWhiteSpace(descricao)
            ? (idProduto == ProdutoSuporteAvulso ? "Suporte avulso" : "Renovacao Click Free")
            : descricao;
        var idTipoPagamento = await ResolverTipoPagamentoAsync(conexao, billingType);

        SqlException? ultimoErro = null;
        for (var tentativa = 0; tentativa < 3; tentativa++)
        {
            var idPedidoBanco = await ProximoIdAsync(conexao, "PEDIDO_CABECALHO");
            var idDetalhe = await ProximoIdAsync(conexao, "PEDIDO_DETALHE");
            var idPagamento = await ProximoIdAsync(conexao, "PEDIDO_VEND_TIPO_PAGAMENTO");

            await using var tx = await conexao.BeginTransactionAsync();
            try
            {
                await using (var cmd = NovoComando("""
                    INSERT INTO PEDIDO_CABECALHO
                    (ID, ID_PEDIDO_APP, RAZAO_CLIENTE, DATA_EMISSAO, VALOR_DESCONTO, SUB_TOTAL, VALOR_PEDIDO, VALOR_RECEBIDO,
                     ID_EMPRESA, ID_VENDEDOR, ID_SITUACAO, ID_TIPOPEDIDO, ID_TABELA_PRECO, VALOR_PRODUTO, ID_CLIENTE, CFOP, DATA_SAIDA, OBS, VALOR_OUTROS)
                    VALUES
                    (@Id, @IdPedidoApp, @RazaoCliente, @DataEmissao, 0, @Valor, @Valor, @Valor,
                     1, 1, 8, 1, 1, @Valor, @IdCliente, 259, @DataEmissao, @Obs, 0);
                    """, conexao, (SqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@Id", idPedidoBanco);
                    cmd.Parameters.AddWithValue("@IdPedidoApp", idPedidoApp > 0 ? idPedidoApp : 0);
                    cmd.Parameters.AddWithValue("@RazaoCliente", Trunc(nomeCliente, 150) ?? cnpj);
                    cmd.Parameters.AddWithValue("@DataEmissao", dataEmissao);
                    cmd.Parameters.AddWithValue("@Valor", valor);
                    cmd.Parameters.AddWithValue("@IdCliente", cliente);
                    cmd.Parameters.AddWithValue("@Obs", Trunc(descricao, 250) ?? "");
                    await cmd.ExecuteNonQueryAsync();
                }

                await using (var cmd = NovoComando("""
                    INSERT INTO PEDIDO_DETALHE
                    (ID, ID_PEDIDO_DETALHE, NOME_PRODUTO, VALOR_DESCPROM, QTDE, VALOR_PRODUTO, VALOR_TOTAL, ID_PRODUTO, ITEM, ID_UNIDADE, ITEM_TROCA, VALOR_DESCONTO, CANCELADO,
                     ID_TIPO_ESTOQUE_PRODUTO, ID_TIPO_PRECO_PRODUTO, ID_EMPRESA_ESTOQUE, ACRESCIMO)
                    VALUES
                    (@Id, @IdPedidoBanco, @NomeProduto, 0, 1, @Valor, @Valor,
                     @ProdutoId, 1, 1, 0, 0, 'N', 1, 1, 1, 0);
                    """, conexao, (SqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@Id", idDetalhe);
                    cmd.Parameters.AddWithValue("@IdPedidoBanco", idPedidoBanco);
                    cmd.Parameters.AddWithValue("@NomeProduto", Trunc(nomeProduto, 150) ?? "Click Free");
                    cmd.Parameters.AddWithValue("@Valor", valor);
                    cmd.Parameters.AddWithValue("@ProdutoId", idProduto);
                    await cmd.ExecuteNonQueryAsync();
                }

                await using (var cmd = NovoComando("""
                    INSERT INTO PEDIDO_VEND_TIPO_PAGAMENTO
                    (ID, ID_PEDIDO_DETALHE, ID_ECF_TIPO_PAGAMENTO, VALOR, VALOR_TOTAL)
                    VALUES
                    (@Id, @IdPedidoBanco, @IdTipoPagamento, @Valor, @Valor);
                    """, conexao, (SqlTransaction)tx))
                {
                    cmd.Parameters.AddWithValue("@Id", idPagamento);
                    cmd.Parameters.AddWithValue("@IdPedidoBanco", idPedidoBanco);
                    cmd.Parameters.AddWithValue("@IdTipoPagamento", idTipoPagamento);
                    cmd.Parameters.AddWithValue("@Valor", valor);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                _logger.LogInformation(
                    "Pedido ClickFree gravado PEDIDO_CABECALHO id={Id} cliente={Cliente} produto={Produto} cnpj={Cnpj}",
                    idPedidoBanco, cliente, idProduto, cnpj);

                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["status"] = "1",
                    ["erro"] = "Pedido inserido no SQL.",
                    ["id"] = idPedidoBanco,
                    ["id_cliente"] = cliente,
                    ["id_produto"] = idProduto
                };
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                await tx.RollbackAsync();
                ultimoErro = ex;
                _logger.LogWarning(ex, "Colisao de ID no pedido Click, tentativa {Tentativa}", tentativa + 1);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        if (ultimoErro is not null)
            throw ultimoErro;

        throw new InvalidOperationException("Nao foi possivel inserir o pedido no SQL.");

    }

    private static CommandDefinition Cmd(string sql, object? param = null) =>
        new(sql, param, commandTimeout: SqlTimeoutSegundos, cancellationToken: CancellationToken.None);

    private static SqlCommand NovoComando(string sql, SqlConnection conexao, SqlTransaction tx)
    {
        return new SqlCommand(sql, conexao, tx) { CommandTimeout = SqlTimeoutSegundos };
    }

    private static async Task<int> ProximoIdAsync(SqlConnection conexao, string tabela)
    {
        var proximo = await conexao.ExecuteScalarAsync<int?>(
            Cmd($"SELECT COALESCE(MAX(ID), 0) + 1 FROM {tabela} WITH (NOLOCK)"));
        return proximo is > 0 ? proximo.Value : 1;
    }

    private static async Task<int> ObterOuCriarClienteAsync(SqlConnection conexao, string cnpj, string nome)
    {
        var id = await conexao.ExecuteScalarAsync<int?>(
            Cmd(
                $"""
                SELECT TOP 1 ID FROM CLIENTE WITH (NOLOCK)
                WHERE {CpfCnpjLookup.ClienteWhere}
                """,
                CpfCnpjLookup.Args(cnpj)));

        if (id is > 0)
            return id.Value;

        var tipo = cnpj.Length == 14 ? "J" : "F";
        var nomeOk = string.IsNullOrWhiteSpace(nome) ? cnpj : nome;

        return await conexao.ExecuteScalarAsync<int>(
            Cmd(
                """
                DECLARE @NovoID INT = (SELECT COALESCE(MAX(ID),0) + 1 FROM CLIENTE WITH (NOLOCK));
                INSERT INTO CLIENTE (ID, ID_SITUACAO_CLIENTE, DATA_CADASTRO, CPF_CNPJ, NOME, TIPO_PESSOA)
                VALUES (@NovoID, 1, CONVERT(date, GETDATE()), @cnpj, @nome, @tipo);
                SELECT @NovoID;
                """,
                new { cnpj, nome = Trunc(nomeOk, 150), tipo }));
    }

    private static async Task<int> ResolverTipoPagamentoAsync(SqlConnection conexao, string billingType)
    {
        var like = billingType.Contains("PIX", StringComparison.OrdinalIgnoreCase)
            ? "%PIX%"
            : billingType.Contains("CREDIT", StringComparison.OrdinalIgnoreCase) || billingType.Contains("CART", StringComparison.OrdinalIgnoreCase)
                ? "%CART%"
                : billingType.Contains("BOLETO", StringComparison.OrdinalIgnoreCase)
                    ? "%BOLETO%"
                    : "%PIX%";

        var id = await conexao.ExecuteScalarAsync<int?>(
            Cmd(
                "SELECT TOP 1 ID FROM ECF_TIPO_PAGAMENTO WITH (NOLOCK) WHERE DESCRICAO LIKE @like ORDER BY ID",
                new { like }));

        return id ?? 1;
    }

    private static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static string? Trunc(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= max ? value : value[..max];
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

    private static decimal? ReadDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;
            var raw = (el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString())?.Trim();
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
