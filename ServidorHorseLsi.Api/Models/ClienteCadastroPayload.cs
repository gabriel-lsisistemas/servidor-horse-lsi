using System.Globalization;
using System.Text.Json;

namespace ServidorHorseLsi.Api.Models;

/// <summary>
/// Campos aceitos no JSON de cadastro (formulário LSIClick).
/// Chaves em minúsculas; aliases legados (CNPJ, razao_social) também aceitos.
/// </summary>
public sealed class ClienteCadastroPayload
{
    public string? CpfCnpj { get; private set; }
    public string? Nome { get; private set; }
    public string? Fantasia { get; private set; }
    public string? TipoPessoa { get; private set; }
    public string? Cep { get; private set; }
    public string? Logradouro { get; private set; }
    public string? Numero { get; private set; }
    public string? Complemento { get; private set; }
    public string? Bairro { get; private set; }
    public string? Cidade { get; private set; }
    public string? Uf { get; private set; }
    public string? PontoReferenciaEndereco { get; private set; }
    public string? Fone1 { get; private set; }
    public string? Fone2 { get; private set; }
    public string? Contato { get; private set; }
    public int? IdSegmento { get; private set; }
    public string? EmailFinanceiro { get; private set; }
    public string? ContatoFinanceiro { get; private set; }
    public string? CpfRepLegal { get; private set; }
    public string? RespLegal { get; private set; }
    public DateTime? DataNascimento { get; private set; }
    public string? Observacao { get; private set; }
    public int? IdSituacaoCliente { get; private set; }

    public static ClienteCadastroPayload FromJsonDocument(JsonDocument? doc)
    {
        var payload = new ClienteCadastroPayload();
        if (doc is null)
            return payload;

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return payload;

        payload.CpfCnpj = FirstString(root, "cpf_cnpj", "cnpj", "CNPJ", "documento");
        payload.Nome = FirstString(root, "nome", "razao_social", "razao social");
        payload.Fantasia = FirstString(root, "fantasia", "nome_fantasia", "nome fantasia");
        payload.TipoPessoa = NormalizeTipoPessoa(FirstString(root, "tipo_pessoa", "tipo_cliente", "tipo de cliente"));
        payload.Cep = FirstString(root, "cep");
        payload.Logradouro = FirstString(root, "logradouro", "endereco", "endereço");
        payload.Numero = FirstString(root, "numero", "número");
        payload.Complemento = FirstString(root, "complemento");
        payload.Bairro = FirstString(root, "bairro");
        payload.Cidade = FirstString(root, "cidade");
        payload.Uf = FirstString(root, "uf", "estado");
        payload.PontoReferenciaEndereco = FirstString(root, "ponto_referencia_endereco", "ponto_referencia", "ponto de referencia");
        payload.Fone1 = FirstString(root, "fone1", "fone", "telefone");
        payload.Fone2 = FirstString(root, "fone2", "fone_financeiro");
        payload.Contato = FirstString(root, "contato");
        payload.IdSegmento = FirstInt(root, "id_segmento", "segmento");
        payload.EmailFinanceiro = FirstString(root, "email_financeiro", "email");
        payload.ContatoFinanceiro = FirstString(root, "contato_financeiro");
        payload.CpfRepLegal = FirstString(root, "cpf_replegal", "cpf_administrador", "cpf administrador");
        payload.RespLegal = FirstString(root, "resp_legal", "nome_administrador", "nome administrador");
        payload.DataNascimento = FirstDate(root, "data_nascimento", "data nascimento");
        payload.Observacao = FirstString(root, "observacao", "obs");
        payload.IdSituacaoCliente = FirstInt(root, "status_cliente", "statis_cliente", "id_situacao_cliente");

        return payload;
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(CpfCnpj))
            errors.Add("cpf_cnpj é obrigatório");
        if (string.IsNullOrWhiteSpace(Nome))
            errors.Add("nome (razão social) é obrigatório");
        return errors;
    }

    private static string? FirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return prop.Value.ValueKind switch
                {
                    JsonValueKind.String => NullIfEmpty(prop.Value.GetString()),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.Null => null,
                    _ => NullIfEmpty(prop.Value.ToString())
                };
            }
        }

        return null;
    }

    private static int? FirstInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n))
                    return n;

                if (prop.Value.ValueKind == JsonValueKind.String &&
                    int.TryParse(prop.Value.GetString(), out var parsed))
                    return parsed;
            }
        }

        return null;
    }

    private static DateTime? FirstDate(JsonElement root, params string[] names)
    {
        var text = FirstString(root, names);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;

        if (DateTime.TryParse(text, new CultureInfo("pt-BR"), DateTimeStyles.None, out dt))
            return dt.Date;

        return null;
    }

    private static string? NormalizeTipoPessoa(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim();
        if (v.Equals("J", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("Jur", StringComparison.OrdinalIgnoreCase))
            return "J";

        if (v.Equals("F", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("Fís", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("Fis", StringComparison.OrdinalIgnoreCase))
            return "F";

        return v.Length >= 1 ? v[..1].ToUpperInvariant() : null;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal object ToInsertParameters(int id, int idSituacaoCliente, DateTime now)
    {
        return new
        {
            ID = id,
            ID_SITUACAO_CLIENTE = IdSituacaoCliente ?? idSituacaoCliente,
            NOME = Nome,
            FANTASIA = Fantasia,
            TIPO_PESSOA = TipoPessoa,
            CPF_CNPJ = CpfCnpj,
            CEP = Cep,
            LOGRADOURO = Logradouro,
            NUMERO = Numero,
            COMPLEMENTO = Complemento,
            BAIRRO = Bairro,
            CIDADE = Cidade,
            UF = Uf,
            PONTO_REFERENCIA_ENDERECO = PontoReferenciaEndereco,
            FONE1 = Fone1,
            FONE2 = Fone2,
            CONTATO = Contato,
            ID_SEGMENTO = IdSegmento,
            EMAIL = EmailFinanceiro,
            EMAIL_FINANCEIRO = EmailFinanceiro,
            CONTATO_FINANCEIRO = ContatoFinanceiro,
            CPF_REPLEGAL = CpfRepLegal,
            RESP_LEGAL = RespLegal,
            DATA_NASCIMENTO = DataNascimento,
            OBSERVACAO = Observacao,
            DATA_CADASTRO = now.Date,
            DATA_ALTERACAO = now,
            DATA_ULTIMA_ALTERACAO = now.Date
        };
    }

    internal object ToUpdateParameters(int id, DateTime now) => new
    {
        ID = id,
        NOME = Nome,
        FANTASIA = Fantasia,
        TIPO_PESSOA = TipoPessoa,
        CPF_CNPJ = CpfCnpj,
        CEP = Cep,
        LOGRADOURO = Logradouro,
        NUMERO = Numero,
        COMPLEMENTO = Complemento,
        BAIRRO = Bairro,
        CIDADE = Cidade,
        UF = Uf,
        PONTO_REFERENCIA_ENDERECO = PontoReferenciaEndereco,
        FONE1 = Fone1,
        FONE2 = Fone2,
        CONTATO = Contato,
        ID_SEGMENTO = IdSegmento,
        EMAIL = EmailFinanceiro,
        EMAIL_FINANCEIRO = EmailFinanceiro,
        CONTATO_FINANCEIRO = ContatoFinanceiro,
        CPF_REPLEGAL = CpfRepLegal,
        RESP_LEGAL = RespLegal,
        DATA_NASCIMENTO = DataNascimento,
        OBSERVACAO = Observacao,
        ID_SITUACAO_CLIENTE = IdSituacaoCliente,
        DATA_ALTERACAO = now,
        DATA_ULTIMA_ALTERACAO = now.Date
    };
}
