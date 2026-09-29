using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using ServidorHorseLsi.Api.Configuration;
using ServidorHorseLsi.Api.Infrastructure;
using ServidorHorseLsi.Api.Models;

namespace ServidorHorseLsi.Api.Services;

public interface IClienteService
{
    Task<Dictionary<string, object?>> RetornaCnpjAsync(string cnpj, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> RetornaCnpjFreeAsync(string cnpj, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> CadastraCnpjAsync(JsonDocument? body, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object?>> CadastraCnpjFreeAsync(JsonDocument? body, CancellationToken cancellationToken = default);
}

public sealed class ClienteService : IClienteService
{
    private const string InsertSql = """
        INSERT INTO CLIENTE (
            ID, ID_SITUACAO_CLIENTE, NOME, FANTASIA, TIPO_PESSOA, CPF_CNPJ,
            CEP, LOGRADOURO, NUMERO, COMPLEMENTO, BAIRRO, CIDADE, UF,
            PONTO_REFERENCIA_ENDERECO, FONE1, FONE2, CONTATO, ID_SEGMENTO,
            EMAIL, EMAIL_FINANCEIRO, CONTATO_FINANCEIRO, CPF_REPLEGAL, RESP_LEGAL,
            DATA_NASCIMENTO, OBSERVACAO, DATA_CADASTRO, DATA_ALTERACAO, DATA_ULTIMA_ALTERACAO
        ) VALUES (
            @ID, @ID_SITUACAO_CLIENTE, @NOME, @FANTASIA, @TIPO_PESSOA, @CPF_CNPJ,
            @CEP, @LOGRADOURO, @NUMERO, @COMPLEMENTO, @BAIRRO, @CIDADE, @UF,
            @PONTO_REFERENCIA_ENDERECO, @FONE1, @FONE2, @CONTATO, @ID_SEGMENTO,
            @EMAIL, @EMAIL_FINANCEIRO, @CONTATO_FINANCEIRO, @CPF_REPLEGAL, @RESP_LEGAL,
            @DATA_NASCIMENTO, @OBSERVACAO, @DATA_CADASTRO, @DATA_ALTERACAO, @DATA_ULTIMA_ALTERACAO
        )
        """;

    private const string UpdateSql = """
        UPDATE CLIENTE SET
            NOME = @NOME,
            FANTASIA = @FANTASIA,
            TIPO_PESSOA = @TIPO_PESSOA,
            CPF_CNPJ = @CPF_CNPJ,
            CEP = @CEP,
            LOGRADOURO = @LOGRADOURO,
            NUMERO = @NUMERO,
            COMPLEMENTO = @COMPLEMENTO,
            BAIRRO = @BAIRRO,
            CIDADE = @CIDADE,
            UF = @UF,
            PONTO_REFERENCIA_ENDERECO = @PONTO_REFERENCIA_ENDERECO,
            FONE1 = @FONE1,
            FONE2 = @FONE2,
            CONTATO = @CONTATO,
            ID_SEGMENTO = @ID_SEGMENTO,
            EMAIL = @EMAIL,
            EMAIL_FINANCEIRO = @EMAIL_FINANCEIRO,
            CONTATO_FINANCEIRO = @CONTATO_FINANCEIRO,
            CPF_REPLEGAL = @CPF_REPLEGAL,
            RESP_LEGAL = @RESP_LEGAL,
            DATA_NASCIMENTO = @DATA_NASCIMENTO,
            OBSERVACAO = @OBSERVACAO,
            ID_SITUACAO_CLIENTE = COALESCE(@ID_SITUACAO_CLIENTE, ID_SITUACAO_CLIENTE),
            DATA_ALTERACAO = @DATA_ALTERACAO,
            DATA_ULTIMA_ALTERACAO = @DATA_ULTIMA_ALTERACAO
        WHERE ID = @ID
        """;

    private readonly ISqlConnectionFactory _connections;
    private readonly ClienteCadastroOptions _cadastroOptions;

    public ClienteService(ISqlConnectionFactory connections, IOptions<ClienteCadastroOptions> cadastroOptions)
    {
        _connections = connections;
        _cadastroOptions = cadastroOptions.Value;
    }

    public async Task<Dictionary<string, object?>> RetornaCnpjAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        return await RetornaCnpjEmBancoAsync(cnpj, clickfree: false, cancellationToken);
    }

    public async Task<Dictionary<string, object?>> RetornaCnpjFreeAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        return await RetornaCnpjEmBancoAsync(cnpj, clickfree: true, cancellationToken);
    }

    public Task<Dictionary<string, object?>> CadastraCnpjAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default) =>
        CadastraCnpjEmBancoAsync(body, clickfree: false, cancellationToken);

    public Task<Dictionary<string, object?>> CadastraCnpjFreeAsync(
        JsonDocument? body,
        CancellationToken cancellationToken = default) =>
        CadastraCnpjEmBancoAsync(body, clickfree: true, cancellationToken);

    private async Task<Dictionary<string, object?>> RetornaCnpjEmBancoAsync(
        string cnpj,
        bool clickfree,
        CancellationToken cancellationToken)
    {
        var documento = DigitsOnly(cnpj);
        if (documento.Length is not (11 or 14))
            return JsonRowHelper.StatusObject("2", "Não existe cliente com esse CNPJ");

        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var row = await conexao.QueryFirstOrDefaultAsync<dynamic>(
            new CommandDefinition(
                $"""
                SELECT TOP 1 *
                FROM cliente WITH (NOLOCK)
                WHERE {CpfCnpjLookup.ClienteWhere}
                """,
                CpfCnpjLookup.Args(documento),
                commandTimeout: 60,
                cancellationToken: CancellationToken.None));

        if (row is not null)
            return JsonRowHelper.MergeRow((object?)row, ("status", "1"), ("erro", "Cliente encontrado"));

        return JsonRowHelper.StatusObject("2", "Não existe cliente com esse CNPJ");
    }

    private async Task<Dictionary<string, object?>> CadastraCnpjEmBancoAsync(
        JsonDocument? body,
        bool clickfree,
        CancellationToken cancellationToken)
    {
        var payload = ClienteCadastroPayload.FromJsonDocument(body);
        var validation = payload.Validate();
        if (validation.Count > 0)
            return JsonRowHelper.MergeRow(null, ("status", "2"), ("erro", string.Join("; ", validation)));

        var cpfCnpj = DigitsOnly(payload.CpfCnpj);
        if (cpfCnpj.Length is not (11 or 14))
            return JsonRowHelper.StatusObject("2", "cpf_cnpj é obrigatório");

        await using var conexao = _connections.CreateGestor();
        await conexao.OpenWithLabelAsync("Gestor", "GESTOR", cancellationToken);

        var existingId = await conexao.ExecuteScalarAsync<int?>(
            $"""
            SELECT TOP 1 ID FROM CLIENTE
            WHERE {CpfCnpjLookup.ClienteWhere}
            """,
            CpfCnpjLookup.Args(cpfCnpj));

        var now = DateTime.Now;
        string operacao;

        if (existingId is > 0)
        {
            await conexao.ExecuteAsync(UpdateSql, payload.ToUpdateParameters(existingId.Value, now));
            operacao = "update";
        }
        else
        {
            var newId = await conexao.ExecuteScalarAsync<int>(
                "SELECT ISNULL(MAX(ID), 0) + 1 FROM CLIENTE WITH (UPDLOCK, HOLDLOCK)");

            await conexao.ExecuteAsync(
                InsertSql,
                payload.ToInsertParameters(newId, _cadastroOptions.IdSituacaoClientePadrao, now));

            existingId = newId;
            operacao = "insert";
        }

        var row = await conexao.QueryFirstOrDefaultDynamicAsync(
            "SELECT ID, CPF_CNPJ, NOME, FANTASIA, CIDADE, UF FROM CLIENTE WHERE ID = @id",
            new { id = existingId.Value });

        return JsonRowHelper.MergeRow(
            (object?)row,
            ("status", "1"),
            ("erro", operacao == "insert" ? "Cliente cadastrado com sucesso" : "Cliente atualizado com sucesso"),
            ("operacao", operacao),
            ("banco", clickfree ? "GESTOR_FREE" : "GESTOR"));
    }

    private static string DigitsOnly(string? value) => CpfCnpjLookup.DigitsOnly(value);
}
