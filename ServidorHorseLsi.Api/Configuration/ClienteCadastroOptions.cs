namespace ServidorHorseLsi.Api.Configuration;

public sealed class ClienteCadastroOptions
{
    public const string SectionName = "ClienteCadastro";

    /// <summary>Obrigatório na tabela CLIENTE (NOT NULL). Padrão típico: 1 = ativo.</summary>
    public int IdSituacaoClientePadrao { get; set; } = 1;
}
