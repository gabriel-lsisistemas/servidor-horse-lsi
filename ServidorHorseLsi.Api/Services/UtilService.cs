using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Services;

public interface IUtilService
{
    Task<Dictionary<string, object?>> DataServidorAsync(CancellationToken cancellationToken = default);
}

public sealed class UtilService : IUtilService
{
    private readonly ISqlConnectionFactory _connections;

    public UtilService(ISqlConnectionFactory connections) => _connections = connections;

    public async Task<Dictionary<string, object?>> DataServidorAsync(CancellationToken cancellationToken = default)
    {
        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "select CONVERT(VARCHAR(10), GETDATE(), 105) as DATA");

        if (row is not null)
            return JsonRowHelper.ToLowerDictionary((object?)row);

        return JsonRowHelper.StatusObject("2", "erro ao obter a data atual!");
    }
}
