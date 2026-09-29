using Dapper;
using System.Data.SqlClient;

namespace ServidorHorseLsi.Api.Infrastructure;

public static class DapperExtensions
{
    public static async Task<dynamic?> QueryFirstOrDefaultDynamicAsync(
        this SqlConnection connection,
        string sql,
        object? param = null)
    {
        return await connection.QueryFirstOrDefaultAsync<dynamic>(sql, param);
    }

    public static async Task<IReadOnlyList<dynamic>> QueryDynamicListAsync(
        this SqlConnection connection,
        string sql,
        object? param = null)
    {
        var rows = await connection.QueryAsync(sql, param);
        return rows.Cast<dynamic>().ToList();
    }
}
