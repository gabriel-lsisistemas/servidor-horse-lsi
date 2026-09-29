using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServidorHorseLsi.Tests;

public class ApiAuthIntegrationTests : IClassFixture<WebApplicationFactoryWithoutSqlValidation>
{
    private readonly WebApplicationFactoryWithoutSqlValidation _factory;

    public ApiAuthIntegrationTests(WebApplicationFactoryWithoutSqlValidation factory) => _factory = factory;

    [Fact]
    public async Task Ping_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/ping", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithValidAuth_EchoesBody()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = CreateBasicAuth("admin.lsisistemas", "mK=9bFJ774iT;iB-");

        var body = """{"hello":"world"}""";
        var response = await client.PostAsync("/ping", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello", content);
    }

    private static AuthenticationHeaderValue CreateBasicAuth(string user, string password)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }
}
