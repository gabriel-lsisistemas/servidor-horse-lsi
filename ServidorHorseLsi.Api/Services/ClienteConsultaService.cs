using Dapper;
using System.Data.SqlClient;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IClienteConsultaService
{
    Task<Dictionary<string, object?>> ConsultaCnpjAsync(string cnpj, int idLocalInstalacao, CancellationToken cancellationToken = default);
}

public sealed class ClienteConsultaService : IClienteConsultaService
{
    private readonly ISqlConnectionFactory _connections;
    private readonly LicenseValidityState _validityState;

    public ClienteConsultaService(ISqlConnectionFactory connections, LicenseValidityState validityState)
    {
        _connections = connections;
        _validityState = validityState;
    }

    public async Task<Dictionary<string, object?>> ConsultaCnpjAsync(string cnpj, int idLocalInstalacao, CancellationToken cancellationToken = default)
    {
        _validityState.VenceuLicenca = false;

        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);
        await using var conexaoLicenca = _connections.CreateGestorLsiClick();
        await conexaoLicenca.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

        var cliente = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select id, NOME, FANTASIA from cliente where CPF_CNPJ=@cnpj",
            new { cnpj });

        if (cliente is not null)
        {
            if (await VerificaExisteLicencaAsync(conexaoLicenca, cnpj, idLocalInstalacao, "LSICLICK", cancellationToken))
                return await ConsultaLicencaLsiClickAsync(conexao, conexaoLicenca, cnpj, idLocalInstalacao, cancellationToken);

            await using var conexaoFree = _connections.CreateGestorFree();
            await conexaoFree.OpenWithLabelAsync("GestorFree", "GESTOR_FREE", cancellationToken);

            if (await VerificaExisteLicencaAsync(conexaoFree, cnpj, 1, "CLICKFREE", cancellationToken))
                return await RetornaClienteClickFreeAsync(conexao, conexaoFree, cnpj, cancellationToken);

            var semLicenca = await conexaoFree.QueryFirstOrDefaultDynamicAsync(
                "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj and ID_LOCAL_INSTALACAO=@idLocal",
                new { cnpj, idLocal = idLocalInstalacao });

            return JsonRowHelper.MergeRow((object?)semLicenca,
                ("status", "1"),
                ("erro", "Cliente sem licenca ativa, verificar o motivo do chamado"));
        }

        await using var conexaoFreeOnly = _connections.CreateGestorFree();
        await conexaoFreeOnly.OpenWithLabelAsync("GestorFree", "GESTOR_FREE", cancellationToken);

        var clienteFree = await conexaoFreeOnly.QueryFirstOrDefaultDynamicAsync(
            "select id, razao_social as NOME, nome_fantasia as fantasia from cliente where cnpj=@cnpj",
            new { cnpj });

        if (clienteFree is not null)
        {
            if (await VerificaExisteLicencaAsync(conexaoFreeOnly, cnpj, idLocalInstalacao, "CLICKFREE", cancellationToken))
                return await RetornaClienteClickFreeAsync(conexao, conexaoFreeOnly, cnpj, cancellationToken);

            var licView = await conexaoFreeOnly.QueryFirstOrDefaultDynamicAsync(
                "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
                new { cnpj });

            return JsonRowHelper.MergeRow((object?)licView,
                ("status", "9"),
                ("erro", "Cliente free sem licenca ativada"));
        }

        return JsonRowHelper.StatusObject("10", "Não existe cliente com esse CNPJ");
    }

    private async Task<Dictionary<string, object?>> ConsultaLicencaLsiClickAsync(
        SqlConnection conexao,
        SqlConnection conexaoLicenca,
        string cnpj,
        int idLocalInstalacao,
        CancellationToken cancellationToken)
    {
        var licenca = await conexaoLicenca.QueryFirstOrDefaultDynamicAsync(
            "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj and ID_LOCAL_INSTALACAO=@idLocal",
            new { cnpj, idLocal = idLocalInstalacao });

        if (licenca is null)
            return JsonRowHelper.StatusObject("1", "Cliente sem licenca ativa, verificar o motivo do chamado");

        var dict = JsonRowHelper.ToLowerDictionary((object?)licenca);
        var dataValidade = GetDate(dict, "data_validade");
        var egestor = GetString(dict, "gestor") ?? "N";

        var situacao = egestor == "N"
            ? LicenseValidityHelper.RetornaValidadeLicenca(dataValidade, "LSICLICK", _validityState)
            : LicenseValidityHelper.RetornaValidadeLicenca(dataValidade, "GESTOR", _validityState);

        if (egestor != "N")
        {
            var gestor = JsonRowHelper.MergeRow((object?)licenca,
                ("status", "12"),
                ("tipo", "Gestor em Garantia"),
                ("erro", situacao));
            return gestor;
        }

        const string tipoContrato = "Contrato";
        var parcelasAtraso = (await conexao.QueryDynamicListAsync(
            "select * from vw_api_lst_contas_receber_aberto where cpf_cnpj=@cnpj and CONVERT(date, DATA_VENCIMENTO)<CONVERT(date,getdate()) order by DATA_VENCIMENTO desc",
            new { cnpj })).ToList();

        if (parcelasAtraso.Count > 0)
        {
            var primeira = parcelasAtraso[0];
            var dataVencimento = GetDate(JsonRowHelper.ToLowerDictionary((object?)primeira), "data_vencimento");
            var diferencaDias = LicenseValidityHelper.DaysBetween(DateTime.Today, dataVencimento);

            var result = JsonRowHelper.MergeRow((object?)primeira,
                ("status", "7"),
                ("tipo", tipoContrato),
                ("erro", diferencaDias > 5
                    ? $"Cliente está com pendência financeira há ({diferencaDias}) dias"
                    : $"Cliente está com pendência. Carência de 5 dias, parcelas vencida há ({diferencaDias}) dias."));

            if (_validityState.VenceuLicenca)
                result["aviso_vencimento"] = "sim";

            return result;
        }

        var qtdAberto = await conexao.ExecuteScalarAsync<int>(
            "select coalesce(count(id_parcela_detalhe),0) from vw_api_lst_contas_receber_aberto where cpf_cnpj=@cnpj",
            new { cnpj });

        if (qtdAberto > 0)
        {
            var ok = JsonRowHelper.MergeRow((object?)licenca,
                ("status", "8"),
                ("tipo", tipoContrato),
                ("erro", "Cliente com manutencao em dias"));

            if (_validityState.VenceuLicenca)
                ok["aviso_vencimento"] = "sim";

            return ok;
        }

        return await RetornaSuporteAvulsoClickAsync(conexao, conexaoLicenca, cnpj, cancellationToken);
    }

    internal async Task<bool> VerificaExisteLicencaAsync(
        SqlConnection conexao,
        string cnpj,
        int idLocalInstalacao,
        string sistemaEsperado,
        CancellationToken cancellationToken)
    {
        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj and ID_LOCAL_INSTALACAO=@idLocal",
            new { cnpj, idLocal = idLocalInstalacao });

        if (row is null)
            return false;

        var dict = JsonRowHelper.ToLowerDictionary((object?)row);
        var sistema = GetString(dict, "sistema") ?? "";

        if (!sistema.Equals("LSICLICK", StringComparison.OrdinalIgnoreCase))
            return true;

        var dataValidade = GetDate(dict, "data_validade");
        var diferencaDias = LicenseValidityHelper.DaysBetween(DateTime.Today, dataValidade);
        return diferencaDias < 150;
    }

    internal async Task<Dictionary<string, object?>> RetornaClienteClickFreeAsync(
        SqlConnection conexaoClick,
        SqlConnection conexaoFree,
        string cnpj,
        CancellationToken cancellationToken)
    {
        var temContrato = await conexaoClick.QueryFirstOrDefaultDynamicAsync(
            "SELECT * FROM vw_api_clientefree_tem_contrato where cpf_cnpj=@cnpj",
            new { cnpj });

        if (temContrato is not null)
        {
            var licFree = await conexaoFree.QueryFirstOrDefaultDynamicAsync(
                "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
                new { cnpj });

            var dataValidade = GetDate(JsonRowHelper.ToLowerDictionary((object?)licFree!), "data_validade");
            LicenseValidityHelper.RetornaValidadeLicenca(dataValidade, "CLICKFREE", _validityState);

            var parcelasAtraso = await conexaoClick.QueryDynamicListAsync(
                "select * from vw_api_lst_contas_receber_aberto where cpf_cnpj=@cnpj and CONVERT(date, DATA_VENCIMENTO)<CONVERT(date,getdate()) order by DATA_VENCIMENTO asc",
                new { cnpj });

            if (parcelasAtraso.Count > 0)
            {
                var result = JsonRowHelper.MergeRow((object?)licFree,
                    ("status", "2"),
                    ("erro", "Cliente esta com pendencias financeira"),
                    ("validade licenca", $"licenca com validade até: {dataValidade:dd/MM/yyyy}"));

                if (_validityState.VenceuLicenca)
                    result["aviso_vencimento"] = "sim";

                return result;
            }

            var ok = JsonRowHelper.MergeRow((object?)licFree,
                ("status", "3"),
                ("erro", "Cliente clickfree com manutencao em dias"),
                ("validade licenca", $"licenca com validade ate: {dataValidade:dd/MM/yyyy}"));

            if (_validityState.VenceuLicenca)
                ok["aviso_vencimento"] = "sim";

            return ok;
        }

        var parcelas = await conexaoClick.QueryDynamicListAsync(
            "select * from vw_api_lst_contas_receber_aberto where cpf_cnpj=@cnpj order by DATA_VENCIMENTO asc",
            new { cnpj });

        if (parcelas.Count == 0)
            return await ClickFreeSemContratoSemParcelasAsync(conexaoClick, conexaoFree, cnpj, cancellationToken);

        return await ClickFreeSemContratoComParcelasAsync(conexaoFree, parcelas, cnpj, cancellationToken);
    }

    private async Task<Dictionary<string, object?>> ClickFreeSemContratoSemParcelasAsync(
        SqlConnection conexaoClick,
        SqlConnection conexaoFree,
        string cnpj,
        CancellationToken cancellationToken)
    {
        var licFree = await conexaoFree.QueryFirstOrDefaultDynamicAsync(
            "select * from vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
            new { cnpj });

        if (licFree is not null)
        {
            var dataValidade = GetDate(JsonRowHelper.ToLowerDictionary((object?)licFree), "data_validade");
            LicenseValidityHelper.RetornaValidadeLicenca(dataValidade, "CLICKFREE", _validityState);
        }

        var vendas = await conexaoClick.QueryDynamicListAsync(
            "select * from vw_api_cliente_tem_venda_click where cpf_cnpj=@cnpj and ID_PRODUTO in (19,36,205,2298) order by DATA_FINALIZADO desc",
            new { cnpj });

        if (vendas.Count == 0)
        {
            var licView = await conexaoFree.QueryFirstOrDefaultDynamicAsync(
                "SELECT * FROM vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
                new { cnpj });

            var result = JsonRowHelper.MergeRow((object?)licView,
                ("status", "4"),
                ("erro", "Cliente ClickFree sem pedidos de garantia (NÃO PODE) "));

            if (_validityState.VenceuLicenca)
                result["aviso_vencimento"] = "sim";

            return result;
        }

        return await ProcessarGarantiaVendaAsync(conexaoFree, vendas[0], false, cnpj);
    }

    private async Task<Dictionary<string, object?>> ClickFreeSemContratoComParcelasAsync(
        SqlConnection conexaoFree,
        IReadOnlyList<dynamic> parcelas,
        string cnpj,
        CancellationToken cancellationToken)
    {
        var primeira = JsonRowHelper.ToLowerDictionary((object?)parcelas[0]);
        var dataVencimento = GetDate(primeira, "data_vencimento");
        string situacao;

        if (dataVencimento.Date > DateTime.Today)
            situacao = "Cliente clickfree, sem contrato lançado mais com manutenção em dias!";
        else
        {
            var diferencaDias = LicenseValidityHelper.DaysBetween(DateTime.Today, dataVencimento);
            situacao = diferencaDias > 5
                ? $"Cliente está com pendência financeira há ({diferencaDias}) dias"
                : $"Cliente está com pendência. Carência de 5 dias, parcelas vencida há ({diferencaDias}) dias.";
        }

        var licView = await conexaoFree.QueryFirstOrDefaultDynamicAsync(
            "SELECT * FROM vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
            new { cnpj });

        var result = JsonRowHelper.MergeRow((object?)licView,
            ("status", "14"),
            ("erro", situacao),
            ("data_pedido", "Data do pedido: " + DateTime.MinValue.ToString("dd/MM/yyyy")));

        if (_validityState.VenceuLicenca)
            result["aviso_vencimento"] = "sim";

        return result;
    }

    internal async Task<Dictionary<string, object?>> RetornaSuporteAvulsoClickAsync(
        SqlConnection conexao,
        SqlConnection conexaoLicenca,
        string cnpj,
        CancellationToken cancellationToken)
    {
        var vendas = await conexao.QueryDynamicListAsync(
            "select * from vw_api_cliente_tem_venda_click where cpf_cnpj=@cnpj and ID_PRODUTO in (80,36,1503,3383,127,82,320,2256,437,42,3,135,3473) order by DATA_FINALIZADO desc",
            new { cnpj });

        if (vendas.Count == 0)
        {
            var licView = await conexaoLicenca.QueryFirstOrDefaultDynamicAsync(
                "SELECT * FROM vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj",
                new { cnpj });

            var result = JsonRowHelper.MergeRow((object?)licView,
                ("status", "13"),
                ("erro", "Cliente LSIClick sem pedidos de garantia"));

            if (_validityState.VenceuLicenca)
                result["aviso_vencimento"] = "sim";

            return result;
        }

        return await ProcessarGarantiaVendaAsync(conexaoLicenca, vendas[0], true, cnpj);
    }

    private async Task<Dictionary<string, object?>> ProcessarGarantiaVendaAsync(
        SqlConnection conexaoLic,
        dynamic vendaRow,
        bool isLsiClick,
        string cnpj)
    {
        var venda = JsonRowHelper.ToLowerDictionary((object?)vendaRow);
        var idProduto = GetInt(venda, "id_produto");
        var dataVenda = GetDate(venda, "data_finalizado");
        var produto = GetString(venda, "produto") ?? "";

        var diasGarantia = ResolveDiasGarantia(idProduto, isLsiClick);
        var diasAtual = LicenseValidityHelper.RetornaValidadeSuporte(dataVenda);

        if (diasAtual > diasGarantia)
        {
            var diasRestantes = diasAtual - diasGarantia;
            return await BuildGarantiaResponseAsync(conexaoLic, cnpj, "5",
                $"Garantia de suporte ja venceu a {diasRestantes} - Cobrar Suporte avulso ", dataVenda, isLsiClick);
        }

        if (isLsiClick)
        {
            var diasRestantesOk = diasGarantia - diasAtual;
            if (diasRestantesOk > 0)
            {
                return await BuildGarantiaResponseAsync(conexaoLic, cnpj, "6",
                    $"O cliente ainda tem {diasRestantesOk} de garantia para: {produto} - Pode Atender",
                    dataVenda, isLsiClick);
            }

            var diasRestantesVenc = (diasAtual + 1) - diasGarantia;
            return await BuildGarantiaResponseAsync(conexaoLic, cnpj, "5",
                $"Garantia de suporte ja venceu a {diasRestantesVenc} - Cobrar Suporte avulso ",
                dataVenda, isLsiClick);
        }

        var diasRestantesFree = diasGarantia - diasAtual;
        return await BuildGarantiaResponseAsync(conexaoLic, cnpj, "6",
            $"O cliente ainda tem {diasRestantesFree} de garantia para: {produto} - Pode Atender",
            dataVenda, isLsiClick);
    }

    private async Task<Dictionary<string, object?>> BuildGarantiaResponseAsync(
        SqlConnection conexao,
        string cnpj,
        string status,
        string erro,
        DateTime dataVenda,
        bool isLsiClick)
    {
        var sqlLic = isLsiClick
            ? "SELECT * FROM vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj"
            : "SELECT * FROM vw_api_cliente_tem_licenca where CPF_CNPJ=@cnpj";

        var licView = await conexao.QueryFirstOrDefaultDynamicAsync(sqlLic, new { cnpj });

        var result = JsonRowHelper.MergeRow((object?)licView,
            ("status", status),
            ("erro", erro),
            ("data_pedido", $"Data do pedido: {dataVenda:dd/MM/yyyy}"));

        if (_validityState.VenceuLicenca)
            result["aviso_vencimento"] = "sim";

        return result;
    }

    private static int ResolveDiasGarantia(int idProduto, bool isLsiClick)
    {
        if (!isLsiClick)
        {
            return idProduto switch
            {
                19 or 205 => 30,
                36 or 2298 => 7,
                _ => 30
            };
        }

        if (idProduto == 3383) return 90;
        if (idProduto == 36) return 7;
        if (idProduto == 80) return 30;
        return 30;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int GetInt(IReadOnlyDictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null)
            return 0;
        return Convert.ToInt32(v);
    }

    private static DateTime GetDate(IReadOnlyDictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v) || v is null)
            return DateTime.MinValue;

        if (v is DateTime dt)
            return dt;

        if (DateTime.TryParse(v.ToString(), out var parsed))
            return parsed;

        return DateTime.MinValue;
    }
}
