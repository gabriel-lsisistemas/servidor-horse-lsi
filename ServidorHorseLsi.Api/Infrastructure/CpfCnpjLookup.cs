namespace ServidorHorseLsi.Api.Infrastructure;

public static class CpfCnpjLookup
{
    public static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());

    public static string Mask(string digits)
    {
        if (digits.Length == 14)
            return $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}";

        if (digits.Length == 11)
            return $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}";

        return digits;
    }

    public static object Args(string digits) => new { digits, masked = Mask(digits) };

    public const string ClienteWhere = "CPF_CNPJ IN (@digits, @masked)";
    public const string ViewWhere = "cpf_cnpj IN (@digits, @masked)";
}
