namespace ServidorHorseLsi.Api.Configuration;

public sealed class CobraSaasOptions
{
    public const string SectionName = "CobraSaas";

    public string BaseUrl { get; set; } = "https://painel.lsisistemas.com.br/cobrasaas/rest.php";
    public string RestKey { get; set; } = "";
    public int SystemUnitId { get; set; } = 1;
    public int BatchSize { get; set; } = 80;
}
