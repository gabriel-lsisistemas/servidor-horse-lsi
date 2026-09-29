using Dapper;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface ILicencaService
{
    Task<Dictionary<string, object?>> LiberarCnpjAsync(string cnpj, string sistema, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> LiberarCnpjIdAsync(string idLicenca, string idRetornoApi, string sistema, string cnpj, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> AtualizaStatusLicencaAsync(int idLicenca, int idStatusLicenca, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> ExisteLicencaCnpjAsync(string cnpj, string idSistema, string localInstalacao, string tipoLicenca, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> AtivaLicencaCnpjAsync(string cnpj, string idSistema, string localInstalacao, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> ExisteLicencaCnpjFreeAsync(string cnpj, string idSistema, string localInstalacao, string tipoLicenca, CancellationToken cancellationToken = default);
}

public sealed class LicencaService : ILicencaService
{
    private readonly ISqlConnectionFactory _connections;
    private readonly IClienteConsultaService _consulta;

    public LicencaService(ISqlConnectionFactory connections, IClienteConsultaService consulta)
    {
        _connections = connections;
        _consulta = consulta;
    }

    public async Task<Dictionary<string, object?>> LiberarCnpjAsync(string cnpj, string sistema, CancellationToken cancellationToken = default)
    {
        var consulta = await _consulta.ConsultaCnpjAsync(cnpj, 1, cancellationToken);
        var sistemaConsultado = consulta.GetValueOrDefault("sistema")?.ToString() ?? "";

        if (!sistemaConsultado.Equals(sistema, StringComparison.OrdinalIgnoreCase))
            return JsonRowHelper.StatusObject("2", "Conflito de licença atual com a liberada, consulta o suporte técnico");

        if (!int.TryParse(consulta.GetValueOrDefault("status")?.ToString(), out var idStatus))
            idStatus = 0;

        if (idStatus is 3 or 8 or 14)
        {
            if (!int.TryParse(consulta.GetValueOrDefault("id")?.ToString(), out var idLicenca))
                return JsonRowHelper.StatusObject("3", "Licença não está pronta para renovação!");

            await using var conexao = _connections.CreateGestorLsiClick();
            await conexao.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

            await conexao.ExecuteAsync(
                "update sac_licenca set data_validade=@dataValidade, id_sac_status_licenca=6 where id=@id",
                new { dataValidade = DateTime.Today.AddDays(30), id = idLicenca });

            return consulta;
        }

        return JsonRowHelper.StatusObject("3", "Licença não está pronta para renovação!");
    }

    public async Task<Dictionary<string, object?>> LiberarCnpjIdAsync(
        string idLicenca,
        string idRetornoApi,
        string sistema,
        string cnpj,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(idRetornoApi, out var idRetornoApiInt))
            return JsonRowHelper.StatusObject("2", "Licença não está pronta para renovação!");

        var allowed = new[] { 3, 8, 14, 2, 4, 5, 6, 7, 12, 13 };
        if (!allowed.Contains(idRetornoApiInt))
            return JsonRowHelper.StatusObject("2", "Licença não está pronta para renovação!");

        if (!int.TryParse(idLicenca, out var idLicencaInt))
            return JsonRowHelper.StatusObject("2", "Licença erro na atualização, verificar com o suporte!");

        var atualizou = false;

        if (!sistema.Equals("CLICK FREE", StringComparison.OrdinalIgnoreCase))
        {
            await using var conexao = _connections.CreateGestorLsiClick();
            await conexao.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

            await conexao.ExecuteAsync(
                "update sac_licenca set data_validade=@dataValidade, id_sac_status_licenca=6 where id=@id",
                new { dataValidade = DateTime.Today.AddDays(60), id = idLicencaInt });

            atualizou = true;

            if (sistema is "LSICLICK" or "LSI CLICK")
            {
                var idSac = await conexao.ExecuteScalarAsync<int?>(
                    "select id_sac_licenca from sac_licenca where id=@id",
                    new { id = idLicencaInt });

                if (idSac.HasValue)
                {
                    await using var conexaoClick = _connections.CreateGestorLsiClick();
                    await conexaoClick.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

                    await conexaoClick.ExecuteAsync(
                        "update licenca set data_validade=@dataValidade, id_status_licenca=6 where id_original=@idOriginal",
                        new { dataValidade = DateTime.Today.AddDays(60), idOriginal = idSac.Value });
                }
            }
        }

        if (sistema.Equals("CLICK FREE", StringComparison.OrdinalIgnoreCase))
        {
            await using var conexaoFree = _connections.CreateGestorFree();
            await conexaoFree.OpenWithLabelAsync("GestorFree", "GESTOR_FREE", cancellationToken);

            await conexaoFree.ExecuteAsync(
                "UPDATE licenca SET data_validade=@dataValidade, id_status_licenca=6 WHERE id=@id",
                new { dataValidade = DateTime.Now.AddYears(1), id = idLicencaInt });

            atualizou = true;
        }

        return atualizou
            ? JsonRowHelper.StatusObject("1", "Licença renovada com sucesso!")
            : JsonRowHelper.StatusObject("2", "Licença erro na atualização, verificar com o suporte!");
    }

    public async Task<Dictionary<string, object?>> AtualizaStatusLicencaAsync(
        int idLicenca,
        int idStatusLicenca,
        CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestorLsiClick();
        await conexao.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

        var rows = await conexao.ExecuteAsync(
            "update sac_licenca set id_sac_status_licenca=@status where id=@id",
            new { status = idStatusLicenca, id = idLicenca });

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select id from sac_licenca where id=@id",
            new { id = idLicenca });

        if (row is not null)
            return JsonRowHelper.ToLowerDictionary((object?)row);

        return JsonRowHelper.StatusObject("2", "erro ao obter a data atual!");
    }

    public async Task<Dictionary<string, object?>> ExisteLicencaCnpjAsync(
        string cnpj,
        string idSistema,
        string localInstalacao,
        string tipoLicenca,
        CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestorLsiClick();
        await conexao.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select * from vw_api_cliente_tem_licenca where cpf_cnpj=@cnpj and ID_LOCAL_INSTALACAO=@local and ID_SAC_SISTEMA=@sistema and ID_TIPO_LICENCA=@tipo and id_tipo_licenca<>8",
            new
            {
                cnpj,
                local = int.Parse(localInstalacao),
                sistema = int.Parse(idSistema),
                tipo = int.Parse(tipoLicenca)
            });

        if (row is not null)
            return JsonRowHelper.MergeRow((object?)row, ("status", "1"), ("erro", "encontrado licença"));

        return JsonRowHelper.StatusObject("2", "Não existe licença liberada para esse CNPJ!");
    }

    public async Task<Dictionary<string, object?>> AtivaLicencaCnpjAsync(
        string cnpj,
        string idSistema,
        string localInstalacao,
        CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestorLsiClick();
        await conexao.OpenWithLabelAsync("GestorLsiClick", "GESTOR_LSICLICK", cancellationToken);

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select * from vw_api_cliente_tem_licenca where cpf_cnpj=@cnpj and ID_LOCAL_INSTALACAO=@local and ID_SAC_SISTEMA=@sistema and id_tipo_licenca<>8",
            new { cnpj, local = int.Parse(localInstalacao), sistema = int.Parse(idSistema) });

        if (row is not null)
            return JsonRowHelper.MergeRow((object?)row, ("status", "1"), ("erro", "encontrado licença"));

        return JsonRowHelper.StatusObject("2", "Não existe licença liberada para esse CNPJ!");
    }

    public async Task<Dictionary<string, object?>> ExisteLicencaCnpjFreeAsync(
        string cnpj,
        string idSistema,
        string localInstalacao,
        string tipoLicenca,
        CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestorFree();
        await conexao.OpenWithLabelAsync("GestorFree", "GESTOR_FREE", cancellationToken);

        var documento = CpfCnpjLookup.DigitsOnly(cnpj);
        if (documento.Length is not (11 or 14))
            return JsonRowHelper.StatusObject("2", "Não existe licença liberada para esse CNPJ!");

        var local = int.TryParse(localInstalacao, out var localId) ? localId : 1;
        var temTipo = int.TryParse(tipoLicenca, out var tipoId);
        var sql = $"""
            SELECT TOP 1 *
            FROM vw_api_cliente_tem_licenca
            WHERE {CpfCnpjLookup.ViewWhere}
              AND ID_LOCAL_INSTALACAO = @local
            """;

        if (temTipo)
            sql += " AND ID_TIPO_LICENCA = @tipo";

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            sql,
            new { digits = documento, masked = CpfCnpjLookup.Mask(documento), local, tipo = tipoId });

        if (row is null && !temTipo)
        {
            row = await conexao.QueryFirstOrDefaultDynamicAsync(
                $"""
                SELECT TOP 1 *
                FROM vw_api_cliente_tem_licenca
                WHERE {CpfCnpjLookup.ViewWhere}
                """,
                CpfCnpjLookup.Args(documento));
        }

        if (row is not null)
            return JsonRowHelper.MergeRow((object?)row, ("status", "1"), ("erro", "encontrado licença"));

        return JsonRowHelper.StatusObject("2", "Não existe licença liberada para esse CNPJ!");
    }
}
