using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using ServidorHorseLsi.Api.Configuration;

namespace ServidorHorseLsi.Api.Middleware;

public sealed class BasicAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BasicAuthOptions _options;

    public BasicAuthenticationMiddleware(RequestDelegate next, IOptions<BasicAuthOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsAnonymousPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!TryAuthenticate(context.Request.Headers.Authorization, out _))
        {
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"ServidorHorseLsi\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }

    private static bool IsAnonymousPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value is "/" or "")
            return true;

        return value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAuthenticate(string? authorizationHeader, out string? username)
    {
        username = null;
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return false;

        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator <= 0)
            return false;

        username = decoded[..separator];
        var password = decoded[(separator + 1)..];

        return username.Equals(_options.Username, StringComparison.Ordinal) &&
               password.Equals(_options.Password, StringComparison.Ordinal);
    }
}
