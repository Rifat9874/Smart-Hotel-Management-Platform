using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace HotelManagement.Web.Data;

/// <summary>
/// All database access goes through here. Every value is sent as a SQL parameter
/// (the WinForms code built SQL by string concatenation, which is unsafe on a public website).
///
/// Usage:  var rows = await db.QueryAsync("SELECT * FROM ROOM_TABLE WHERE ROOM_ID = @id", new { id });
/// </summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is missing in appsettings.json");
    }

    // The only line in the whole project that touches Microsoft.Data.SqlClient directly.
    private DbConnection CreateConnection() => new SqlConnection(_connectionString);

    public async Task<List<Row>> QueryAsync(string sql, object? args = null)
    {
        await using var con = CreateConnection();
        await con.OpenAsync();
        return await Runner.QueryAsync(con, null, sql, args);
    }

    public async Task<Row?> QueryOneAsync(string sql, object? args = null)
    {
        var rows = await QueryAsync(sql, args);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>First column of the first row (COUNT(*), MAX(...) ...). Returns default(T) when empty/NULL.</summary>
    public async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var con = CreateConnection();
        await con.OpenAsync();
        return await Runner.ScalarAsync<T>(con, null, sql, args);
    }

    /// <summary>INSERT / UPDATE / DELETE. Returns the number of affected rows.</summary>
    public async Task<int> ExecAsync(string sql, object? args = null)
    {
        await using var con = CreateConnection();
        await con.OpenAsync();
        return await Runner.ExecAsync(con, null, sql, args);
    }

    /// <summary>Runs a whole script (several statements) as one batch. Used by /Setup.</summary>
    public Task<int> ExecScriptAsync(string script) => ExecAsync(script);

    public async Task<DbTx> BeginAsync()
    {
        var con = CreateConnection();
        await con.OpenAsync();
        var tx = await con.BeginTransactionAsync();
        return new DbTx(con, tx);
    }

    /// <summary>Cheap connectivity test used by the /Setup page.</summary>
    public async Task<(bool ok, string message)> PingAsync()
    {
        try
        {
            await using var con = CreateConnection();
            await con.OpenAsync();
            return (true, "Connected");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

/// <summary>A transaction: several statements that succeed or fail together.</summary>
public sealed class DbTx : IAsyncDisposable
{
    private readonly DbConnection _con;
    private readonly DbTransaction _tx;
    private bool _done;

    internal DbTx(DbConnection con, DbTransaction tx)
    {
        _con = con;
        _tx = tx;
    }

    public Task<List<Row>> QueryAsync(string sql, object? args = null) => Runner.QueryAsync(_con, _tx, sql, args);
    public Task<T?> ScalarAsync<T>(string sql, object? args = null) => Runner.ScalarAsync<T>(_con, _tx, sql, args);
    public Task<int> ExecAsync(string sql, object? args = null) => Runner.ExecAsync(_con, _tx, sql, args);

    public async Task CommitAsync()
    {
        await _tx.CommitAsync();
        _done = true;
    }

    public async Task RollbackAsync()
    {
        if (_done) return;
        await _tx.RollbackAsync();
        _done = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_done)
        {
            try { await _tx.RollbackAsync(); } catch { /* connection already broken */ }
        }
        await _tx.DisposeAsync();
        await _con.DisposeAsync();
    }
}

/// <summary>Shared helpers that work on any ADO.NET connection (so they never mention SqlClient).</summary>
internal static class Runner
{
    internal static DbCommand Build(DbConnection con, DbTransaction? tx, string sql, object? args)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.CommandTimeout = 30;

        if (args != null)
        {
            foreach (var prop in args.GetType().GetProperties())
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@" + prop.Name;
                var value = prop.GetValue(args);
                if (value is DateOnly d) value = d.ToDateTime(TimeOnly.MinValue);
                p.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
        }
        return cmd;
    }

    internal static async Task<List<Row>> QueryAsync(DbConnection con, DbTransaction? tx, string sql, object? args)
    {
        await using var cmd = Build(con, tx, sql, args);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
        {
            var row = new Row();
            for (int i = 0; i < reader.FieldCount; i++)
                row.Set(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }

    internal static async Task<T?> ScalarAsync<T>(DbConnection con, DbTransaction? tx, string sql, object? args)
    {
        await using var cmd = Build(con, tx, sql, args);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull) return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(result, target, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task<int> ExecAsync(DbConnection con, DbTransaction? tx, string sql, object? args)
    {
        await using var cmd = Build(con, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }
}
