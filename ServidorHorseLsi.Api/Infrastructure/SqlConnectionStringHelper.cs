using System.Data.SqlClient;

namespace ServidorHorseLsi.Api.Infrastructure;

public static class SqlConnectionStringHelper
{
    public const int DefaultPort = 1433;
    private const string DefaultApplicationName = "ServidorHorseLsi.Api";

    /// <summary>
    /// Builds a connection string using the same SQL Server options used in SSMS.
    /// Escapes special characters in user/password (e.g. 321+_)(sa).
    /// </summary>
    public static string Build(string server, string database, string user, string password, int port = DefaultPort)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = FormatDataSource(server, port),
            InitialCatalog = database,
            PersistSecurityInfo = false,
            UserID = user,
            Password = password,
            Pooling = true,
            MaxPoolSize = 20,
            MinPoolSize = 0,
            ConnectTimeout = 15,
            MultipleActiveResultSets = false,
            Encrypt = false,
            TrustServerCertificate = true,
            ApplicationName = DefaultApplicationName
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Normalizes connection strings from appsettings to the options used in SSMS.
    /// </summary>
    public static string Normalize(string connectionString, int port = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is empty.", nameof(connectionString));

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            PersistSecurityInfo = false,
            Pooling = true,
            MaxPoolSize = 20,
            MinPoolSize = 0,
            ConnectTimeout = 15,
            MultipleActiveResultSets = false,
            Encrypt = false,
            TrustServerCertificate = true
        };

        builder.ApplicationName = DefaultApplicationName;

        builder.DataSource = FormatDataSource(builder.DataSource, port);
        return builder.ConnectionString;
    }

    private static string FormatDataSource(string server, int port)
    {
        return server.Trim();
    }
}
