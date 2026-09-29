using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IFinanceiroService
{
    Task<IReadOnlyList<Dictionary<string, object?>>> ParcelasEmAbertoAsync(string cnpj, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Dictionary<string, object?>>> ParcelasPagasAsync(string cnpj, CancellationToken cancellationToken = default);
}

public sealed class FinanceiroService : IFinanceiroService
{
    private readonly ISqlConnectionFactory _connections;

    public FinanceiroService(ISqlConnectionFactory connections) => _connections = connections;

    public Task<IReadOnlyList<Dictionary<string, object?>>> ParcelasEmAbertoAsync(string cnpj, CancellationToken cancellationToken = default) =>
        ListarParcelasAsync(cnpj, "Cliente sem parcelas em aberto!", cancellationToken);

    public Task<IReadOnlyList<Dictionary<string, object?>>> ParcelasPagasAsync(string cnpj, CancellationToken cancellationToken = default) =>
        ListarParcelasAsync(cnpj, "Cliente sem parcelas Pagas!", cancellationToken);

    private async Task<IReadOnlyList<Dictionary<string, object?>>> ListarParcelasAsync(
        string cnpj,
        string mensagemVazio,
        CancellationToken cancellationToken)
    {
        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var rows = await conexao.QueryDynamicListAsync(
            "select * from vw_api_lst_contas_receber_aberto where cpf_cnpj=@cnpj order by DATA_VENCIMENTO asc",
            new { cnpj });

        if (rows.Count > 0)
            return JsonRowHelper.ToLowerList(rows);

        return new List<Dictionary<string, object?>>
        {
            JsonRowHelper.StatusObject("2", mensagemVazio)
        };
    }
}
