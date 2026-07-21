using System.Text.Json.Nodes;

namespace DotCarbon.Plugins.Sql;

/// <summary>Open (or create) a database. <c>Db</c> is ":memory:", a file name, or "sqlite:&lt;file&gt;".</summary>
public record SqlLoadArgs(string Db);

/// <summary>Run a query against a loaded database. <c>Values</c> bind to <c>$1</c>, <c>$2</c>, … placeholders.</summary>
public record SqlQueryArgs(string Db, string Query, JsonArray? Values = null);

/// <summary>The result of a write query.</summary>
public record SqlExecuteResult(long RowsAffected, long LastInsertId);

/// <summary>A schema migration applied to a database on load, once, in ascending version order.</summary>
public record SqlMigration(string Db, int Version, string Sql, string? Description = null);

/// <summary>Plugin configuration (<c>plugins.sql</c>): migrations to apply when a database is loaded.</summary>
public record SqlOptions(SqlMigration[]? Migrations = null);
