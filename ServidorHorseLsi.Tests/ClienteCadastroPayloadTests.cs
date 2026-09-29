using System.Text.Json;
using ServidorHorseLsi.Api.Models;

namespace ServidorHorseLsi.Tests;

public class ClienteCadastroPayloadTests
{
    [Fact]
    public void FromJsonDocument_MapsFormFields()
    {
        const string json = """
            {
              "cpf_cnpj": "11229442000120",
              "tipo_pessoa": "Jurídica",
              "nome": "LS INFORMATICA E SISTEMAS LTDA - ME",
              "fantasia": "LSI SISTEMAS",
              "cep": "69000000",
              "logradouro": "Rua Teste",
              "numero": "100",
              "bairro": "Centro",
              "cidade": "MANAUS",
              "uf": "AM",
              "fone1": "92999999999",
              "id_segmento": 3,
              "email_financeiro": "fin@teste.com",
              "cpf_replegal": "12345678901",
              "resp_legal": "Admin",
              "data_nascimento": "1980-05-10",
              "observacao": "Obs teste"
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var payload = ClienteCadastroPayload.FromJsonDocument(doc);

        Assert.Empty(payload.Validate());
        Assert.Equal("11229442000120", payload.CpfCnpj);
        Assert.Equal("J", payload.TipoPessoa);
        Assert.Equal("LS INFORMATICA E SISTEMAS LTDA - ME", payload.Nome);
        Assert.Equal(3, payload.IdSegmento);
    }

    [Fact]
    public void FromJsonDocument_MapsStatusClienteToIdSituacao()
    {
        using var doc = JsonDocument.Parse("""{"cpf_cnpj":"1","nome":"X","status_cliente":2}""");
        var payload = ClienteCadastroPayload.FromJsonDocument(doc);
        Assert.Equal(2, payload.IdSituacaoCliente);
    }

    [Fact]
    public void Validate_RequiresCpfAndNome()
    {
        using var doc = JsonDocument.Parse("{}");
        var payload = ClienteCadastroPayload.FromJsonDocument(doc);
        var errors = payload.Validate();
        Assert.Contains(errors, e => e.Contains("cpf_cnpj"));
        Assert.Contains(errors, e => e.Contains("nome"));
    }
}
