using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace FinCore.Benchmarks;

internal static class SqlPlanCapture
{
    public static async Task<IReadOnlyList<SqlPlanSnapshot>> CaptureAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (20)
                qs.execution_count,
                qs.total_elapsed_time,
                qs.total_logical_reads,
                st.text,
                qp.query_plan
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
            WHERE st.dbid = DB_ID()
              AND (
                    st.text LIKE N'%[Trades]%' OR
                    st.text LIKE N'%[MarketPrices]%' OR
                    st.text LIKE N'%[Instruments]%'
                  )
            ORDER BY qs.last_execution_time DESC;
            """;
        command.CommandType = CommandType.Text;

        List<SqlPlanSnapshot> plans = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(new SqlPlanSnapshot(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return plans;
    }

    public static async Task<IReadOnlyList<SqlPlanSnapshot>> CaptureExecutedPlansAsync(
        string connectionString,
        IReadOnlyList<SqlCommandDefinition> commands,
        CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        List<SqlPlanSnapshot> plans = [];

        foreach (SqlCommandDefinition definition in commands
                     .Where(command => command.SqlText.Contains(
                         "[Trades]",
                         StringComparison.OrdinalIgnoreCase) ||
                         command.SqlText.Contains(
                             "[MarketPrices]",
                             StringComparison.OrdinalIgnoreCase) ||
                         command.SqlText.Contains(
                             "[Instruments]",
                             StringComparison.OrdinalIgnoreCase)))
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"SET STATISTICS XML ON;{definition.SqlText}";
            command.CommandTimeout = 120;
            foreach (SqlParameterDefinition parameter in definition.Parameters)
            {
                SqlParameter sqlParameter = command.Parameters.Add(
                    parameter.Name,
                    System.Data.SqlDbType.NVarChar);
                sqlParameter.DbType = parameter.Type;
                sqlParameter.Size = parameter.Size;
                sqlParameter.Precision = parameter.Precision;
                sqlParameter.Scale = parameter.Scale;
                sqlParameter.Value = parameter.Value ?? DBNull.Value;
            }

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            string planXml = string.Empty;
            do
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.FieldCount == 1 &&
                        reader.GetFieldType(0) == typeof(string) &&
                        reader.GetName(0).Equals("Microsoft SQL Server 2005 XML Showplan",
                            StringComparison.Ordinal))
                    {
                        planXml = reader.GetString(0);
                    }
                }
            }
            while (await reader.NextResultAsync(cancellationToken));

            plans.Add(new SqlPlanSnapshot(
                1,
                0,
                0,
                definition.SqlText,
                planXml));
        }

        return plans;
    }
}

internal sealed record SqlPlanSnapshot(
    long ExecutionCount,
    long TotalElapsedMicroseconds,
    long TotalLogicalReads,
    string SqlText,
    string QueryPlanXml);

internal sealed record SqlCommandDefinition(
    string SqlText,
    IReadOnlyList<SqlParameterDefinition> Parameters)
{
    public static SqlCommandDefinition From(DbCommand command)
    {
        return new SqlCommandDefinition(
            command.CommandText,
            command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => new SqlParameterDefinition(
                    parameter.ParameterName,
                    parameter.DbType,
                    parameter.Size,
                    parameter.Precision,
                    parameter.Scale,
                    parameter.Value))
                .ToArray());
    }
}

internal sealed record SqlParameterDefinition(
    string Name,
    System.Data.DbType Type,
    int Size,
    byte Precision,
    byte Scale,
    object? Value);
