using System.Data.SqlClient;

namespace ServidorHorseLsi.Api.Infrastructure;

public static class SqlConnectionExtensions
{
    public static async Task OpenWithLabelAsync(
        this SqlConnection connection,
        string logicalName,
        string iniSection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
            throw new InvalidOperationException(
                $"Falha SQL em [{logicalName}] (secao conexao.conf [{iniSection}] ou ConnectionStrings:{logicalName}). " +
                $"DataSource={builder.DataSource}, Database={builder.InitialCatalog}, User={builder.UserID}. " +
                "Copie USUARIO e SENHA exatos do conexao.conf do servidor Horse que ja funciona. " +
                $"Erro: {ex.Message}",
                ex);
        }
    }
}
