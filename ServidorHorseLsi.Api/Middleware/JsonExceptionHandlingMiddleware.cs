using System.Net;
using ServidorHorseLsi.Api.Infrastructure;

namespace ServidorHorseLsi.Api.Middleware;

public sealed class JsonExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JsonExceptionHandlingMiddleware> _logger;

    public JsonExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<JsonExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro nao tratado na requisicao {Method} {Path}.", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = JsonRowHelper.StatusObject("2", ex.GetBaseException().Message);
            await context.Response.WriteAsync(JsonRowHelper.SerializeIso88591(payload));
        }
    }
}
