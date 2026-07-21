using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Sql;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.6: the sql plugin runs real SQLite (Microsoft.Data.Sqlite works natively on this OS), so these
/// drive execute/select/parameters/migrations end-to-end against in-memory and file databases.
/// </summary>
public class SqlPluginTests : IDisposable
{
    private readonly string _dir;

    public SqlPluginTests() => _dir = Directory.CreateTempSubdirectory("carbon-sql-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class Fixture(SqlPlugin plugin, CarbonApp app) : IAsyncDisposable
    {
        public SqlPlugin Plugin { get; } = plugin;
        public async ValueTask DisposeAsync()
        {
            await Plugin.DisposeAsync();
            app.Shutdown();
        }
    }

    private static async Task<Fixture> Build(object? options = null)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            App = new AppConfig { Identifier = "com.test.sql", Name = "SqlTest" },
        };
        var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
        var handle = app.Start();
        var plugin = new SqlPlugin(handle);
        await plugin.InitializeAsync(new PluginContext(handle, options is null ? null : JsonSerializer.SerializeToElement(options)));
        return new Fixture(plugin, app);
    }

    private static JsonArray Values(params object[] values) =>
        new([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);

    [Fact]
    public async Task Execute_and_select_round_trip_values_and_types()
    {
        await using var fixture = await Build();
        var sql = fixture.Plugin;
        sql.Load(new SqlLoadArgs(":memory:"));

        sql.Execute(new SqlQueryArgs(":memory:", "CREATE TABLE person (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)"));
        var insert = sql.Execute(new SqlQueryArgs(":memory:", "INSERT INTO person (name, age) VALUES ('Ada', 36)"));

        Assert.Equal(1, insert.RowsAffected);
        Assert.Equal(1, insert.LastInsertId);

        var rows = sql.Select(new SqlQueryArgs(":memory:", "SELECT id, name, age FROM person"));
        var row = Assert.Single(rows);
        Assert.Equal(1, row["id"]!.GetValue<long>());
        Assert.Equal("Ada", row["name"]!.GetValue<string>());
        Assert.Equal(36, row["age"]!.GetValue<long>());
    }

    [Fact]
    public async Task Bound_parameters_are_used()
    {
        await using var fixture = await Build();
        var sql = fixture.Plugin;
        sql.Load(new SqlLoadArgs(":memory:"));
        sql.Execute(new SqlQueryArgs(":memory:", "CREATE TABLE item (id INTEGER PRIMARY KEY, label TEXT)"));

        sql.Execute(new SqlQueryArgs(":memory:", "INSERT INTO item (id, label) VALUES ($1, $2)", Values(7, "widget")));

        var rows = sql.Select(new SqlQueryArgs(":memory:", "SELECT label FROM item WHERE id = $1", Values(7)));
        Assert.Equal("widget", Assert.Single(rows)["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Migrations_run_once_and_persist_across_loads()
    {
        var dbPath = Path.Combine(_dir, "app.db");
        object Options() => new
        {
            migrations = new[]
            {
                new { db = dbPath, version = 1, sql = "CREATE TABLE note (id INTEGER PRIMARY KEY, body TEXT)" },
            },
        };

        // First run: the migration creates the table; add a row.
        await using (var first = await Build(Options()))
        {
            first.Plugin.Load(new SqlLoadArgs(dbPath));
            first.Plugin.Execute(new SqlQueryArgs(dbPath, "INSERT INTO note (body) VALUES ('hello')"));
        }

        // Second run (fresh plugin, same file): the migration is already applied, so it doesn't re-run,
        // and the data is still there.
        await using var second = await Build(Options());
        second.Plugin.Load(new SqlLoadArgs(dbPath));

        var applied = second.Plugin.Select(new SqlQueryArgs(dbPath, "SELECT COUNT(*) AS n FROM _carbon_migrations"));
        Assert.Equal(1, applied[0]["n"]!.GetValue<long>());

        var notes = second.Plugin.Select(new SqlQueryArgs(dbPath, "SELECT body FROM note"));
        Assert.Equal("hello", Assert.Single(notes)["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task Query_before_load_throws()
    {
        await using var fixture = await Build();
        Assert.Throws<InvalidOperationException>(
            () => fixture.Plugin.Select(new SqlQueryArgs(":memory:", "SELECT 1")));
    }

    [Fact]
    public async Task Registers_its_commands()
    {
        await using var fixture = await Build();
        var registry = new FakeRegistry();
        fixture.Plugin.Register(registry);

        Assert.Contains("sql:load", registry.Handlers.Keys);
        Assert.Contains("sql:execute", registry.Handlers.Keys);
        Assert.Contains("sql:select", registry.Handlers.Keys);
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<JsonNode?>>> Handlers { get; } = new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler) => Handlers[name] = handler;
    }
}
