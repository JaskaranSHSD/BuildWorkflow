using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildWorkflow.AICenter.Helpers;

public static class SqlDatabaseReader
{
    // Forbidden patterns that indicate potential SQL injection attempts
    private static readonly Regex[] ForbiddenPatterns = new[]
    {
        new Regex(@"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|REPLACE|BULK|OPENROWSET|OPENDATASOURCE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(UNION\s+(ALL\s+)?SELECT)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(WAITFOR\s+DELAY|BENCHMARK\s*\()\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(SLEEP\s*\()\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(XP_CMDSHELL|SP_OACREATE|SP_OAMETHOD)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(INTO\s+(OUTFILE|DUMPFILE))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(LOAD_FILE\s*\()\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"--", RegexOptions.Compiled), // SQL comments
        new Regex(@"/\*", RegexOptions.Compiled), // Block comments start
        new Regex(@"\*/", RegexOptions.Compiled), // Block comments end
        new Regex(@"\b(DECLARE|SET\s+@)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), // Variable declarations
        new Regex(@"\b(CAST|CONVERT)\s*\(\s*\w+\s+AS\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled), // Type casting that could be abused
    };

    private static void ValidateReadOnlyQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("Query cannot be empty.");
        }

        var trimmed = sql.Trim();

        // Must start with SELECT or WITH (for CTEs)
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only read-only SELECT or WITH queries are allowed.");
        }

        // No semicolons (multiple statements)
        trimmed = trimmed.TrimEnd(';').Trim();
        if (trimmed.Contains(';'))
        {
            throw new InvalidOperationException("Multiple SQL statements are not allowed.");
        }

        // Check for forbidden patterns
        foreach (var pattern in ForbiddenPatterns)
        {
            if (pattern.IsMatch(trimmed))
            {
                throw new InvalidOperationException("Query contains forbidden patterns.");
            }
        }

        // Additional validation: ensure no dangerous function calls
        var dangerousFunctions = new[] { "pg_read_file", "pg_write_file", "pg_ls_dir", "xp_", "sp_", "fn_", "utl_file", "dbms_", "sys.", "information_schema." };
        var lowerSql = trimmed.ToLowerInvariant();
        foreach (var func in dangerousFunctions)
        {
            if (lowerSql.Contains(func))
            {
                throw new InvalidOperationException($"Query contains forbidden function: {func}");
            }
        }
    }

    public static async Task<string> GetSchemaAsync(string connectionString, string databaseType, CancellationToken cancellationToken = default)
    {
        var dbType = Normalize(databaseType);
        var grouped = new Dictionary<string, List<Dictionary<string, object?>>>();

        if (dbType == "postgres")
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT table_schema, table_name, column_name, data_type, is_nullable
                FROM information_schema.columns
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                ORDER BY table_schema, table_name, ordinal_position;
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadSchemaAsync(reader, grouped, cancellationToken);
        }
        else if (dbType == "mysql")
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
                """;

            await using var command = new MySqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadSchemaAsync(reader, grouped, cancellationToken);
        }
        else
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
                """;

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadSchemaAsync(reader, grouped, cancellationToken);
        }

        var tables = grouped.Select(pair => new Dictionary<string, object?>
        {
            ["table"] = pair.Key,
            ["columns"] = pair.Value
        }).ToList();

        return JsonSerializer.Serialize(tables, new JsonSerializerOptions { WriteIndented = true });
    }

    public static async Task<DataTable> ExecuteReadOnlyQueryToTableAsync(string connectionString, string databaseType, string sql, CancellationToken cancellationToken = default)
    {
        ValidateReadOnlyQuery(sql);
        var trimmed = sql.Trim().TrimEnd(';').Trim();

        var dbType = Normalize(databaseType);
        var table = new DataTable();

        if (dbType == "postgres")
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadTableAsync(reader, table, cancellationToken);
        }
        else if (dbType == "mysql")
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadTableAsync(reader, table, cancellationToken);
        }
        else
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadTableAsync(reader, table, cancellationToken);
        }

        return table;
    }

    public static async Task<string> ExecuteReadOnlyQueryAsync(string connectionString, string databaseType, string sql, CancellationToken cancellationToken = default)
    {
        ValidateReadOnlyQuery(sql);
        var trimmed = sql.Trim().TrimEnd(';').Trim();

        var dbType = Normalize(databaseType);
        var rows = new List<Dictionary<string, object?>>();

        if (dbType == "postgres")
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadRowsAsync(reader, rows, cancellationToken);
        }
        else if (dbType == "mysql")
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadRowsAsync(reader, rows, cancellationToken);
        }
        else
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(trimmed, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await ReadRowsAsync(reader, rows, cancellationToken);
        }

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task ReadTableAsync(DbDataReader reader, DataTable table, CancellationToken cancellationToken)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            table.Columns.Add(reader.GetName(i), typeof(object));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }
            table.Rows.Add(values);
        }
    }

    private static async Task ReadSchemaAsync(DbDataReader reader, Dictionary<string, List<Dictionary<string, object?>>> grouped, CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var key = $"{schema}.{table}";

            if (!grouped.TryGetValue(key, out var columns))
            {
                columns = new List<Dictionary<string, object?>>();
                grouped[key] = columns;
            }

            columns.Add(new Dictionary<string, object?>
            {
                ["column"] = reader.GetString(2),
                ["type"] = reader.GetString(3),
                ["nullable"] = reader.GetString(4)
            });
        }
    }

    private static async Task ReadRowsAsync(DbDataReader reader, List<Dictionary<string, object?>> rows, CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
    }

    private static string Normalize(string? databaseType) =>
        databaseType?.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pgsql" => "postgres",
            "mysql" => "mysql",
            "mssql" or "sqlserver" or "sql server" or "sql-server" => "sqlserver",
            _ => "sqlserver"
        };
}
