using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace DotCarbon.Plugins.Sql;

/// <summary>
/// SQLite access for the app (Task 6.6) via Microsoft.Data.Sqlite. <c>load</c> opens a database and
/// runs any configured migrations; <c>execute</c> runs writes (returning rows affected and the last
/// insert id); <c>select</c> returns rows as objects. Values bind to <c>$1</c>, <c>$2</c>, … placeholders.
/// </summary>
[CarbonPlugin("SQL", description: "SQLite database access with migrations.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("sql:default", "Allow all sql commands.", Commands = new[] { "sql:*" })]
public partial class SqlPlugin : IPlugin
{
    private readonly AppHandle _app;
    private readonly object _gate = new();
    private readonly Dictionary<string, SqliteConnection> _connections = new(StringComparer.Ordinal);
    private SqlOptions _options = new();

    public SqlPlugin(AppHandle app) => _app = app;

    public string Namespace => "sql";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(SqlJsonContext.Default.SqlOptions);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            foreach (var connection in _connections.Values) connection.Dispose();
            _connections.Clear();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>Open a database (creating it if needed) and apply pending migrations.</summary>
    [CarbonCommand("load")]
    public string Load(SqlLoadArgs args)
    {
        lock (_gate)
        {
            if (!_connections.ContainsKey(args.Db))
            {
                var connection = new SqliteConnection($"Data Source={ResolveDataSource(args.Db)}");
                connection.Open();
                _connections[args.Db] = connection;
                Migrate(args.Db, connection);
            }
        }
        return args.Db;
    }

    /// <summary>Run a write query; returns rows affected and the last inserted row id.</summary>
    [CarbonCommand("execute")]
    public SqlExecuteResult Execute(SqlQueryArgs args)
    {
        var connection = Connection(args.Db);
        using var command = connection.CreateCommand();
        command.CommandText = args.Query;
        Bind(command, args.Values);
        var rows = command.ExecuteNonQuery();
        return new SqlExecuteResult(rows, LastInsertId(connection));
    }

    /// <summary>Run a read query; returns each row as a name → value map.</summary>
    [CarbonCommand("select")]
    public List<Dictionary<string, JsonNode?>> Select(SqlQueryArgs args)
    {
        var connection = Connection(args.Db);
        using var command = connection.CreateCommand();
        command.CommandText = args.Query;
        Bind(command, args.Values);

        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, JsonNode?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = ToJson(reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Close a database.</summary>
    [CarbonCommand("close")]
    public void Close(SqlLoadArgs args)
    {
        lock (_gate)
        {
            if (_connections.Remove(args.Db, out var connection))
                connection.Dispose();
        }
    }

    private SqliteConnection Connection(string db)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(db, out var connection)
                ? connection
                : throw new InvalidOperationException($"Database '{db}' is not loaded. Call load() first.");
        }
    }

    private void Migrate(string db, SqliteConnection connection)
    {
        var migrations = (_options.Migrations ?? [])
            .Where(migration => migration.Db == db)
            .OrderBy(migration => migration.Version)
            .ToList();
        if (migrations.Count == 0) return;

        Run(connection, null,
            "CREATE TABLE IF NOT EXISTS _carbon_migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)");

        long current;
        using (var max = connection.CreateCommand())
        {
            max.CommandText = "SELECT COALESCE(MAX(version), 0) FROM _carbon_migrations";
            current = Convert.ToInt64(max.ExecuteScalar());
        }

        foreach (var migration in migrations.Where(migration => migration.Version > current))
        {
            using var transaction = connection.BeginTransaction();
            Run(connection, transaction, migration.Sql);
            using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO _carbon_migrations (version, applied_at) VALUES ($v, $t)";
                record.Parameters.AddWithValue("$v", migration.Version);
                record.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                record.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private static void Run(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, JsonArray? values)
    {
        if (values is null) return;
        for (var i = 0; i < values.Count; i++)
            command.Parameters.AddWithValue($"${i + 1}", ToParameter(values[i]));
    }

    private static long LastInsertId(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private string ResolveDataSource(string db)
    {
        if (db == ":memory:") return db;

        var name = db.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase) ? db["sqlite:".Length..] : db;
        if (Path.IsPathRooted(name)) return name;

        var dir = Path.Combine(DataRoot(), _app.Config.App.Identifier);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static string DataRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support")
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(home, ".local", "share");
    }

    private static object ToParameter(JsonNode? node)
    {
        if (node is not JsonValue value) return DBNull.Value;
        if (value.TryGetValue<bool>(out var b)) return b ? 1L : 0L;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<string>(out var s)) return s;
        return value.ToJsonString();
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonValue.Create(value.ToString()),
    };
}
