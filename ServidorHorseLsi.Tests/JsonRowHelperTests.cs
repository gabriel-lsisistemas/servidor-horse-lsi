using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Tests;

public class JsonRowHelperTests
{
    [Fact]
    public void MergeRow_AddsStatusAndErroInLowercase()
    {
        var result = JsonRowHelper.MergeRow(null, ("status", "10"), ("erro", "Não existe cliente com esse CNPJ"));

        Assert.Equal("10", result["status"]?.ToString());
        Assert.Equal("Não existe cliente com esse CNPJ", result["erro"]?.ToString());
    }

    [Fact]
    public void ToLowerDictionary_ConvertsPropertyNames()
    {
        var row = new Dictionary<string, object> { ["CPF_CNPJ"] = "123", ["ID"] = 1 };
        var dict = JsonRowHelper.ToLowerDictionary(row);

        Assert.True(dict.ContainsKey("cpf_cnpj"));
        Assert.True(dict.ContainsKey("id"));
        Assert.Equal(1, dict["id"]);
    }

    [Fact]
    public void ToLowerDictionary_SerializesDatesAsHorseIsoUtc()
    {
        var row = new Dictionary<string, object>
        {
            ["DATA_VALIDADE"] = new DateTime(2025, 8, 5, 0, 0, 0, DateTimeKind.Unspecified),
            ["DATA_LICENCA"] = new DateTime(2014, 3, 15, 14, 32, 20, DateTimeKind.Unspecified)
        };

        var dict = JsonRowHelper.ToLowerDictionary(row);
        Assert.Equal("2025-08-05T00:00:00.000Z", dict["data_validade"]);
        Assert.Equal("2014-03-15T14:32:20.000Z", dict["data_licenca"]);
    }

    [Fact]
    public void ToLowerDictionary_PreservesNullAndNumericTypes()
    {
        var row = new Dictionary<string, object?>
        {
            ["ID"] = 1,
            ["QTDE_PDV"] = 10,
            ["LOCAL_INSTALACAO"] = null
        };

        var dict = JsonRowHelper.ToLowerDictionary(row);
        Assert.Equal(1, dict["id"]);
        Assert.Equal(10, dict["qtde_pdv"]);
        Assert.Null(dict["local_instalacao"]);
    }
}
