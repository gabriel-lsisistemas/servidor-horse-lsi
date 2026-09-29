using System.Data.SqlClient;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Tests;

public class SqlConnectionStringHelperTests
{
    [Fact]
    public void Build_EscapesSpecialUserAndUsesSsmsCompatibleOptions()
    {
        var cs = SqlConnectionStringHelper.Build("142.44.252.253", "gestor", "321+_)(sa", "p@ss;word");

        var builder = new SqlConnectionStringBuilder(cs);
        Assert.Equal("321+_)(sa", builder.UserID);
        Assert.Equal("p@ss;word", builder.Password);
        Assert.Equal("142.44.252.253", builder.DataSource);
        Assert.True(builder.Pooling);
        Assert.Equal(20, builder.MaxPoolSize);
        Assert.False(builder.MultipleActiveResultSets);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal("ServidorHorseLsi.Api", builder.ApplicationName);
    }

    [Fact]
    public void Normalize_UsesSsmsCompatibleOptions()
    {
        var raw = "Server=142.44.252.253;Database=gestor;User Id=321+_)(sa;Password=secret;TrustServerCertificate=True;Encrypt=False";
        var normalized = SqlConnectionStringHelper.Normalize(raw);

        var builder = new SqlConnectionStringBuilder(normalized);
        Assert.Equal("142.44.252.253", builder.DataSource);
        Assert.Equal("321+_)(sa", builder.UserID);
        Assert.True(builder.Pooling);
        Assert.Equal(20, builder.MaxPoolSize);
        Assert.False(builder.MultipleActiveResultSets);
        Assert.True(builder.TrustServerCertificate);
        Assert.Equal("ServidorHorseLsi.Api", builder.ApplicationName);
    }
}
