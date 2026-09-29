using System.Data.SqlClient;

namespace ServidorHorseLsi.Api.Infrastructure;

public interface ISqlConnectionFactory
{
    IReadOnlyCollection<SqlConnectionTarget> ConfiguredTargets { get; }
    SqlConnection CreateGestor();
    SqlConnection CreateGestorFree();
    SqlConnection CreateGestorLsiClick();
}

public sealed record SqlConnectionTarget(string Name, string IniSection, Func<SqlConnection> Create);

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string? _gestor;
    private readonly string? _gestorFree;
    private readonly string? _gestorLsiClick;

    public IReadOnlyCollection<SqlConnectionTarget> ConfiguredTargets { get; }

    public SqlConnectionFactory(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SqlConnectionFactory> logger)
    {
        var iniPath = Path.Combine(environment.ContentRootPath, "conexao.conf");
        var iniExists = File.Exists(iniPath);

        _gestor = ResolveConnectionString(configuration, iniPath, iniExists, "GESTOR", "Gestor", logger);
        _gestorFree = ResolveConnectionString(configuration, iniPath, iniExists, "GESTOR_FREE", "GestorFree", logger);
        _gestorLsiClick = ResolveConnectionString(configuration, iniPath, iniExists, "GESTOR_LSICLICK", "GestorLsiClick", logger);

        var targets = new List<SqlConnectionTarget>();
        AddTargetIfConfigured(targets, logger, "Gestor", "GESTOR", _gestor, CreateGestor);
        AddTargetIfConfigured(targets, logger, "GestorFree", "GESTOR_FREE", _gestorFree, CreateGestorFree);
        AddTargetIfConfigured(targets, logger, "GestorLsiClick", "GESTOR_LSICLICK", _gestorLsiClick, CreateGestorLsiClick);
        ConfiguredTargets = targets;
    }

    private static string? ResolveConnectionString(
        IConfiguration configuration,
        string iniPath,
        bool iniExists,
        string iniSection,
        string appsettingsKey,
        ILogger logger)
    {
        if (iniExists)
        {
            if (!IniConfigurationReader.SectionExists(iniPath, iniSection))
            {
                logger.LogInformation(
                    "SQL [{Key}]: secao [{Section}] nao encontrada em conexao.conf; conexao ignorada.",
                    appsettingsKey, iniSection);
                return null;
            }

            var ini = IniConfigurationReader.ReadSection(iniPath, iniSection);
            if (ini.TryGetValue("SERVIDOR", out var server) &&
                ini.TryGetValue("BANCO", out var database) &&
                ini.TryGetValue("USUARIO", out var user) &&
                ini.TryGetValue("SENHA", out var password) &&
                !string.IsNullOrWhiteSpace(server))
            {
                logger.LogInformation(
                    "SQL [{Key}]: usando conexao.conf [{Section}] -> servidor {Server}, banco {Database}, usuario {User}",
                    appsettingsKey, iniSection, server, database, user);

                return SqlConnectionStringHelper.Build(server, database, user, password);
            }

            throw new InvalidOperationException(
                $"A secao [{iniSection}] existe em conexao.conf, mas esta incompleta. " +
                "Informe SERVIDOR, BANCO, USUARIO e SENHA ou remova a secao para ignorar essa conexao.");
        }

        var fromConfig = configuration.GetConnectionString(appsettingsKey);
        if (string.IsNullOrWhiteSpace(fromConfig))
        {
            throw new InvalidOperationException(
                $"Configure ConnectionStrings:{appsettingsKey} em appsettings ou a secao [{iniSection}] em conexao.conf.");
        }

        logger.LogInformation("SQL [{Key}]: usando ConnectionStrings do appsettings", appsettingsKey);
        return SqlConnectionStringHelper.Normalize(fromConfig);
    }

    private static void AddTargetIfConfigured(
        ICollection<SqlConnectionTarget> targets,
        ILogger logger,
        string name,
        string iniSection,
        string? connectionString,
        Func<SqlConnection> create)
    {
        if (connectionString is null)
            return;

        LogSafeTarget(logger, name, connectionString);
        targets.Add(new SqlConnectionTarget(name, iniSection, create));
    }

    private static void LogSafeTarget(ILogger logger, string name, string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        logger.LogInformation(
            "SQL [{Name}] DataSource={DataSource}, Database={Database}, User={User}",
            name, builder.DataSource, builder.InitialCatalog, builder.UserID);
    }

    public SqlConnection CreateGestor() => CreateConfiguredConnection(_gestor, "Gestor", "GESTOR");

    public SqlConnection CreateGestorFree() => CreateConfiguredConnection(_gestorFree, "GestorFree", "GESTOR_FREE");

    public SqlConnection CreateGestorLsiClick() => CreateConfiguredConnection(_gestorLsiClick, "GestorLsiClick", "GESTOR_LSICLICK");

    private static SqlConnection CreateConfiguredConnection(string? connectionString, string name, string iniSection)
    {
        if (connectionString is null)
        {
            throw new InvalidOperationException(
                $"Conexao SQL [{name}] nao configurada. Inclua a secao [{iniSection}] no conexao.conf para usar essa rotina.");
        }

        return new SqlConnection(connectionString);
    }
}
