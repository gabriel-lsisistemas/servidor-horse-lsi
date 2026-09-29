using Dapper;

namespace ServidorHorseLsi.Api.Infrastructure;

public static class SqlConnectionStartupValidator
{
    public static async Task ValidateAsync(ISqlConnectionFactory factory, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (factory.ConfiguredTargets.Count == 0)
        {
            logger.LogWarning("Nenhuma conexao SQL configurada para validar.");
            return;
        }

        var failures = new List<string>();

        foreach (var target in factory.ConfiguredTargets)
        {
            await using var connection = target.Create();
            try
            {
                await connection.OpenWithLabelAsync(target.Name, target.IniSection, cancellationToken);
                await connection.ExecuteScalarAsync(new CommandDefinition(
                    "SELECT 1",
                    commandTimeout: 5,
                    cancellationToken: cancellationToken));
                logger.LogInformation("SQL [{Name}] conexao testada com sucesso.", target.Name);
            }
            catch (Exception ex)
            {
                failures.Add(ex.Message);
                logger.LogError(ex, "SQL [{Name}] falhou no teste de inicializacao.", target.Name);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Uma ou mais conexoes SQL falharam na inicializacao:\n- " + string.Join("\n- ", failures));
        }
    }
}
