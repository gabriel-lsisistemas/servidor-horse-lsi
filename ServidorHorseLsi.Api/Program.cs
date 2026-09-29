using System.Text;
using Dapper;
using Microsoft.OpenApi.Models;
using ServidorHorseLsi.Api.Configuration;
using ServidorHorseLsi.Api.Infrastructure;
using ServidorHorseLsi.Api.Middleware;
using ServidorHorseLsi.Api.Services;
using ServidorHorseLsi.Api.Swagger;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
SqlMapper.Settings.CommandTimeout = 30;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:9001");

builder.Services.Configure<BasicAuthOptions>(builder.Configuration.GetSection(BasicAuthOptions.SectionName));
builder.Services.Configure<ClienteCadastroOptions>(builder.Configuration.GetSection(ClienteCadastroOptions.SectionName));
builder.Services.Configure<CobraSaasOptions>(builder.Configuration.GetSection(CobraSaasOptions.SectionName));

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<LicenseValidityState>();
builder.Services.AddScoped<IClienteConsultaService, ClienteConsultaService>();
builder.Services.AddScoped<ILicencaService, LicencaService>();
builder.Services.AddScoped<IFinanceiroService, FinanceiroService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IContratoService, ContratoService>();
builder.Services.AddScoped<IContaClickService, ContaClickService>();
builder.Services.AddScoped<IVendaClickService, VendaClickService>();
builder.Services.AddScoped<IUtilService, UtilService>();
builder.Services.AddHttpClient<ICobraSaasClient, CobraSaasClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<ICobraSaasReplicadorService, CobraSaasReplicadorService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ServidorHorseLsi API",
        Version = "v1",
        Description = "API de licença/cliente (compatível com o servidor Horse). Use Authorize com Basic Auth para testar."
    });

    options.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        Description = "Usuário e senha de BasicAuth no appsettings."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Basic"
                }
            },
            Array.Empty<string>()
        }
    });

    options.OperationFilter<JsonObjectRequestBodyOperationFilter>();
});

var app = builder.Build();

if (app.Configuration.GetValue("ValidateSqlOnStartup", true))
{
    try
    {
        await SqlConnectionStartupValidator.ValidateAsync(
            app.Services.GetRequiredService<ISqlConnectionFactory>(),
            app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Corrija conexao.conf ou ConnectionStrings antes de atender requisicoes.");
        throw;
    }
}

var enableSwagger = app.Configuration.GetValue("EnableSwagger", true);
if (enableSwagger)
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "swagger/{documentName}/swagger.json";
    });
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("v1/swagger.json", "ServidorHorseLsi v1");
        options.DisplayRequestDuration();
        options.DocumentTitle = "ServidorHorseLsi API";
    });
}

app.UseMiddleware<JsonExceptionHandlingMiddleware>();
app.UseMiddleware<BasicAuthenticationMiddleware>();
app.MapControllers();

if (enableSwagger)
{
    app.MapGet("/", () => Results.Redirect("/swagger/index.html", false));
    app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html", false));
}

var port = builder.Configuration.GetValue<int?>("ServerPort") ?? 9001;
app.Logger.LogInformation("Servidor iniciado na porta {Port}", port);
if (enableSwagger)
    app.Logger.LogInformation("Swagger: http://localhost:{Port}/swagger", port);

app.Run();

public partial class Program;
