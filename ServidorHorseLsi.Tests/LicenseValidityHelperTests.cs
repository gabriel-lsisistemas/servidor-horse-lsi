using ServidorHorseLsi.Api.Services;

namespace ServidorHorseLsi.Tests;

public class LicenseValidityHelperTests
{
    [Fact]
    public void RetornaValidadeLicenca_WhenNotExpired_SetsAvisoWhenWithin15Days()
    {
        var state = new LicenseValidityState();
        var msg = LicenseValidityHelper.RetornaValidadeLicenca(DateTime.Today.AddDays(10), "LSICLICK", state);

        Assert.Contains("Falta 10", msg);
        Assert.True(state.VenceuLicenca);
    }

    [Fact]
    public void RetornaValidadeLicenca_WhenExpired_SetsVenceuLicenca()
    {
        var state = new LicenseValidityState();
        var msg = LicenseValidityHelper.RetornaValidadeLicenca(DateTime.Today.AddDays(-3), "LSICLICK", state);

        Assert.Contains("renovar", msg);
        Assert.True(state.VenceuLicenca);
    }

    [Fact]
    public void DaysBetween_IsAbsolute()
    {
        var a = new DateTime(2026, 1, 10);
        var b = new DateTime(2026, 1, 5);
        Assert.Equal(5, LicenseValidityHelper.DaysBetween(a, b));
        Assert.Equal(5, LicenseValidityHelper.DaysBetween(b, a));
    }
}
