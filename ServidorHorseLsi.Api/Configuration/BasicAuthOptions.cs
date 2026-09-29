namespace ServidorHorseLsi.Api.Configuration;

public sealed class BasicAuthOptions
{
    public const string SectionName = "BasicAuth";

    public string Username { get; set; } = "admin.lsisistemas";
    public string Password { get; set; } = "";
}
