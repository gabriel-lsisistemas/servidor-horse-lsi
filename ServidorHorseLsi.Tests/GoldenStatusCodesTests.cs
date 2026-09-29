using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Tests;

/// <summary>
/// Golden expectations for status codes documented in udmprincipal.pas (lines 3-16).
/// Use captured JSON from the Pascal server in homologation to extend this suite.
/// </summary>
public class GoldenStatusCodesTests
{
    public static TheoryData<string, string> DocumentedStatuses => new()
    {
        { "1", "Sem licença ativa" },
        { "2", "ClickFree pendência financeira" },
        { "3", "ClickFree manutenção em dias" },
        { "7", "LSIClick pendência financeira" },
        { "8", "LSIClick manutenção em dias" },
        { "10", "CNPJ não encontrado" },
        { "12", "Gestor em dias" },
        { "14", "ClickFree sem contrato, manutenção em dias" }
    };

    [Theory]
    [MemberData(nameof(DocumentedStatuses))]
    public void StatusObject_MatchesContract(string status, string _)
    {
        var obj = JsonRowHelper.StatusObject(status, "mensagem de teste");
        Assert.Equal(status, obj["status"]?.ToString());
        Assert.Equal("mensagem de teste", obj["erro"]?.ToString());
    }

    [Fact]
    public void CnpjNotFound_MatchesStatus10()
    {
        var obj = JsonRowHelper.StatusObject("10", "Não existe cliente com esse CNPJ");
        Assert.Equal("10", obj["status"]);
    }
}
