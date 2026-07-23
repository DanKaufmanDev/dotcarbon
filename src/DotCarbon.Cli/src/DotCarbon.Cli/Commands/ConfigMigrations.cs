using System.Text.Json.Nodes;

namespace DotCarbon.Cli.Commands;

/// <summary>One step that upgrades a carbon.json from <see cref="FromVersion"/> to the next version.</summary>
internal interface IConfigMigration
{
    /// <summary>The config version this migration upgrades *from*; it produces <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>One line describing what changed, printed when the migration runs.</summary>
    string Describe { get; }

    /// <summary>Mutates the config object in place. Must be safe to run on already-migrated input.</summary>
    void Apply(JsonObject config);
}

/// <summary>The result of running the engine: the new version and what each step did.</summary>
internal sealed record MigrationOutcome(int FromVersion, int ToVersion, IReadOnlyList<string> Applied)
{
    public bool Changed => ToVersion != FromVersion;
}

/// <summary>
/// Upgrades a carbon.json across schema versions. Configs carry a <c>configVersion</c>; a config
/// without one is treated as version 0 (pre-versioning). The engine applies every pending migration
/// in order and stamps the new version. There are no field-changing migrations yet — the flagship
/// concrete migration in this release is the Tauri import (<see cref="MigrateCommand"/>) — so the
/// engine ships proven and ready rather than waiting for the first breaking change to be designed
/// under pressure.
/// </summary>
internal static class ConfigMigrationEngine
{
    /// <summary>The schema version a config produced by the current CLI is at.</summary>
    public const int CurrentVersion = 1;

    private static readonly IReadOnlyList<IConfigMigration> Migrations =
    [
        new StampVersionMigration(),
    ];

    public static int VersionOf(JsonObject config) =>
        config["configVersion"] is JsonValue value && value.TryGetValue(out int version) ? version : 0;

    /// <summary>Runs the real migration set against <paramref name="config"/>.</summary>
    public static MigrationOutcome Migrate(JsonObject config) => Migrate(config, Migrations, CurrentVersion);

    /// <summary>
    /// Applies the migrations whose <see cref="IConfigMigration.FromVersion"/> is at or above the
    /// config's current version, in ascending order, then stamps <paramref name="targetVersion"/>.
    /// Separated from the real registry so the ordering and idempotency can be tested with synthetic
    /// migrations.
    /// </summary>
    public static MigrationOutcome Migrate(
        JsonObject config, IReadOnlyList<IConfigMigration> migrations, int targetVersion)
    {
        var from = VersionOf(config);
        if (from >= targetVersion)
            return new MigrationOutcome(from, from, []);

        var applied = new List<string>();
        foreach (var migration in migrations.OrderBy(migration => migration.FromVersion))
        {
            if (migration.FromVersion < from || migration.FromVersion >= targetVersion) continue;
            migration.Apply(config);
            applied.Add(migration.Describe);
        }

        StampVersion(config, targetVersion);
        return new MigrationOutcome(from, targetVersion, applied);
    }

    /// <summary>Writes <c>configVersion</c> as the first key, so it reads as a header.</summary>
    internal static void StampVersion(JsonObject config, int version)
    {
        config.Remove("configVersion");
        var rebuilt = new JsonObject { ["configVersion"] = version };
        foreach (var property in config.ToList())
        {
            config.Remove(property.Key);
            rebuilt[property.Key] = property.Value;
        }

        foreach (var property in rebuilt.ToList())
        {
            rebuilt.Remove(property.Key);
            config[property.Key] = property.Value;
        }
    }

    /// <summary>
    /// 0 → 1: adopt the versioned schema. No field changes — versioning was introduced here — so this
    /// only records the version, which lets later migrations know where a config started.
    /// </summary>
    private sealed class StampVersionMigration : IConfigMigration
    {
        public int FromVersion => 0;

        public string Describe => "stamped configVersion (adopting the versioned schema)";

        public void Apply(JsonObject config)
        {
            // The version stamp is written by the engine after every migration runs.
        }
    }
}
