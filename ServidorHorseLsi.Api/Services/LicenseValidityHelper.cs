namespace ServidorHorseLsi.Api.Services;

public static class LicenseValidityHelper
{
    public static int DaysBetween(DateTime a, DateTime b) =>
        Math.Abs((a.Date - b.Date).Days);

    public static string RetornaValidadeLicenca(DateTime dataLicenca, string tipoLicenca, LicenseValidityState state)
    {
        state.VenceuLicenca = false;
        var hoje = DateTime.Today;
        var validade = dataLicenca.Date;

        if (validade >= hoje)
        {
            var dias = DaysBetween(validade, hoje);
            if (dias <= 15)
                state.VenceuLicenca = true;

            return $"Falta {dias} para vencer sua licenca {tipoLicenca}";
        }

        var diasVencido = DaysBetween(hoje, validade);
        state.VenceuLicenca = true;
        return $"Sua licenca {tipoLicenca} venceu a  {diasVencido} dias renovar";
    }

    public static int RetornaValidadeSuporte(DateTime dataVenda) =>
        DaysBetween(DateTime.Today, dataVenda.Date);
}
