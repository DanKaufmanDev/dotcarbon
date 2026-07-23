using System.Text.Json.Nodes;
using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.4: `carbon migrate` — the config-version engine and the Tauri import. The Tauri mapping is
/// pure (it takes config text), so its field mapping and its "did not translate" warnings are covered
/// directly; the command wiring and the version engine are driven against temp directories.
/// </summary>
public class MigrateCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carbon-migrate-{Guid.NewGuid():N}");

    public MigrateCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ---- version engine -----------------------------------------------------------------------

    [Fact]
    public void A_config_with_no_version_is_treated_as_version_zero()
    {
        var config = JsonNode.Parse("""{ "app": { "name": "X" } }""")!.AsObject();

        Assert.Equal(0, ConfigMigrationEngine.VersionOf(config));
    }

    [Fact]
    public void Migrating_stamps_the_current_version_and_reports_the_bump()
    {
        var config = JsonNode.Parse("""{ "app": { "name": "X" } }""")!.AsObject();

        var outcome = ConfigMigrationEngine.Migrate(config);

        Assert.True(outcome.Changed);
        Assert.Equal(0, outcome.FromVersion);
        Assert.Equal(ConfigMigrationEngine.CurrentVersion, outcome.ToVersion);
        Assert.Equal(ConfigMigrationEngine.CurrentVersion, ConfigMigrationEngine.VersionOf(config));
    }

    [Fact]
    public void An_already_current_config_is_a_no_op()
    {
        var config = JsonNode.Parse(
            $$"""{ "configVersion": {{ConfigMigrationEngine.CurrentVersion}}, "app": { "name": "X" } }""")!.AsObject();

        var outcome = ConfigMigrationEngine.Migrate(config);

        Assert.False(outcome.Changed);
        Assert.Empty(outcome.Applied);
    }

    [Fact]
    public void The_version_is_stamped_as_the_first_key_so_it_reads_as_a_header()
    {
        var config = JsonNode.Parse("""{ "app": { "name": "X" }, "build": {} }""")!.AsObject();

        ConfigMigrationEngine.Migrate(config);

        Assert.Equal("configVersion", config.First().Key);
    }

    [Fact]
    public void The_engine_applies_only_pending_migrations_in_order()
    {
        // A config already at v1 must skip the 0->1 step and run only 1->2 and 2->3.
        var config = JsonNode.Parse("""{ "configVersion": 1 }""")!.AsObject();
        var applied = new List<int>();
        IReadOnlyList<IConfigMigration> migrations =
        [
            new RecordingMigration(2, applied),
            new RecordingMigration(0, applied),
            new RecordingMigration(1, applied),
        ];

        var outcome = ConfigMigrationEngine.Migrate(config, migrations, targetVersion: 3);

        Assert.Equal([1, 2], applied);
        Assert.Equal(3, outcome.ToVersion);
        Assert.Equal(3, ConfigMigrationEngine.VersionOf(config));
    }

    [Fact]
    public void Re_running_the_engine_is_idempotent()
    {
        var config = JsonNode.Parse("""{ "app": { "name": "X" } }""")!.AsObject();

        ConfigMigrationEngine.Migrate(config);
        var second = ConfigMigrationEngine.Migrate(config);

        Assert.False(second.Changed);
    }

    // ---- Tauri import: v2 ---------------------------------------------------------------------

    [Fact]
    public void Imports_a_tauri_v2_config()
    {
        var import = TauriConfigImporter.Import(TauriV2, "fallback");

        Assert.Equal("2", import.TauriVersion);
        Assert.Equal("Notes", import.AppName);
        Assert.Equal("com.acme.notes", import.Identifier);
        Assert.Equal("http://localhost:1420", import.DevUrl);

        Assert.Equal("Notes", (string?)import.Carbon["app"]!["name"]);
        Assert.Equal("2.3.1", (string?)import.Carbon["app"]!["version"]);
        Assert.Equal(1024, (int)import.Carbon["window"]!["width"]!);
        Assert.Equal(600, (int)import.Carbon["window"]!["minWidth"]!);
        Assert.Equal("main", (string?)import.Carbon["window"]!["capabilities"]![0]);
        Assert.Equal("dist", (string?)import.Carbon["build"]!["frontendDist"]);
        Assert.Equal("npm run dev", (string?)import.Carbon["build"]!["devCommand"]);
        Assert.Equal("src-carbon", (string?)import.Carbon["build"]!["backendProject"]);
        Assert.Contains("default-src", (string?)import.Carbon["security"]!["csp"]);
        Assert.Equal(ConfigMigrationEngine.CurrentVersion, (int)import.Carbon["configVersion"]!);
    }

    [Fact]
    public void A_frontend_dist_relative_to_src_tauri_is_rebased_to_the_project_root()
    {
        // Tauri's "../dist" is relative to src-tauri/; at the project root it is just "dist".
        var import = TauriConfigImporter.Import(TauriV2, "fallback");

        Assert.Equal("dist", (string?)import.Carbon["build"]!["frontendDist"]);
    }

    [Fact]
    public void Rust_commands_and_tauri_plugins_are_reported_as_not_translated()
    {
        // These are the two things a Tauri user most needs told: the backend language changed, and the
        // plugins have no automatic equivalent. Dropping them silently would strand the user at runtime.
        var import = TauriConfigImporter.Import(TauriV2, "fallback");

        Assert.Contains(import.Warnings, w => w.Contains("[CarbonCommand]"));
        Assert.Contains(import.Warnings, w => w.Contains("shell") && w.Contains("fs"));
    }

    // ---- Tauri import: v1 ---------------------------------------------------------------------

    [Fact]
    public void Imports_a_tauri_v1_config_from_its_different_shape()
    {
        var import = TauriConfigImporter.Import(TauriV1, "fallback");

        Assert.Equal("1", import.TauriVersion);
        // Identity is under `package`; identifier is under `tauri.bundle`; window under `tauri.windows`.
        Assert.Equal("OldApp", import.AppName);
        Assert.Equal("io.legacy.oldapp", import.Identifier);
        Assert.Equal("http://localhost:8080", import.DevUrl);
        // distDir "../public" rebased to the root.
        Assert.Equal("public", (string?)import.Carbon["build"]!["frontendDist"]);
        Assert.False((bool)import.Carbon["window"]!["resizable"]!);
        Assert.Contains(import.Warnings, w => w.Contains("allowlist"));
    }

    [Fact]
    public void A_v1_dev_path_that_is_a_directory_not_a_url_is_flagged()
    {
        // Tauri could serve static files with no dev server; Carbon needs a URL, so this must not be
        // silently written as a devUrl that points at a folder.
        var config = TauriV1.Replace("http://localhost:8080", "../dist");

        var import = TauriConfigImporter.Import(config, "fallback");

        Assert.Null(import.DevUrl);
        Assert.False(import.Carbon["build"]!.AsObject().ContainsKey("devUrl"));
        Assert.Contains(import.Warnings, w => w.Contains("devPath"));
    }

    [Fact]
    public void Missing_identity_falls_back_to_sensible_defaults()
    {
        var import = TauriConfigImporter.Import("""{ "build": {} }""", "my-project");

        Assert.Equal("my-project", import.AppName);
        Assert.Equal("com.example.myproject", import.Identifier);
        Assert.Equal("dist", (string?)import.Carbon["build"]!["frontendDist"]);
    }

    // ---- command wiring -----------------------------------------------------------------------

    [Fact]
    public void Migrate_imports_a_tauri_project_and_scaffolds_the_csharp_side()
    {
        WriteFile("src-tauri/tauri.conf.json", TauriV2);

        Assert.True(MigrateCommand.Run(Request()));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal("Notes", (string?)config["app"]!["name"]);
        // Same scaffold as `carbon init`, because the mobile generators depend on this exact layout.
        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "Notes.csproj")));
        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "capabilities", "main.json")));
        Assert.True(File.Exists(Path.Combine(_root, "src-shared", "AppLogic.csproj")));
    }

    [Fact]
    public void Migrate_upgrades_an_existing_carbon_json_in_place()
    {
        WriteFile("carbon.json", """{ "app": { "name": "Existing" }, "build": { "frontendDist": "dist" } }""");

        Assert.True(MigrateCommand.Run(Request()));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal(ConfigMigrationEngine.CurrentVersion, (int)config["configVersion"]!);
        Assert.Equal("Existing", (string?)config["app"]!["name"]);
    }

    [Fact]
    public void Both_configs_present_is_ambiguous_and_asks_the_user_to_choose()
    {
        WriteFile("carbon.json", """{ "app": { "name": "X" } }""");
        WriteFile("src-tauri/tauri.conf.json", TauriV2);

        Assert.False(MigrateCommand.Run(Request()));
        // --from resolves it.
        Assert.True(MigrateCommand.Run(Request() with { From = "carbon" }));
    }

    [Fact]
    public void Nothing_to_migrate_is_an_error_pointing_at_init()
    {
        Assert.False(MigrateCommand.Run(Request()));
    }

    [Fact]
    public void An_existing_carbon_json_is_not_overwritten_by_a_tauri_import_without_force()
    {
        WriteFile("src-tauri/tauri.conf.json", TauriV2);
        WriteFile("carbon.json", """{ "app": { "name": "Keep" } }""");

        // Auto-detect is ambiguous here, so force the import explicitly; --force lets it overwrite.
        Assert.False(MigrateCommand.Run(Request() with { From = "tauri" }));
        Assert.Contains("Keep", File.ReadAllText(Path.Combine(_root, "carbon.json")));

        Assert.True(MigrateCommand.Run(Request() with { From = "tauri", Force = true }));
        Assert.Contains("Notes", File.ReadAllText(Path.Combine(_root, "carbon.json")));
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        WriteFile("src-tauri/tauri.conf.json", TauriV2);

        Assert.True(MigrateCommand.Run(Request() with { DryRun = true }));

        Assert.False(File.Exists(Path.Combine(_root, "carbon.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "src-carbon")));
    }

    [Fact]
    public void The_imported_config_loads_through_the_real_config_loader()
    {
        WriteFile("src-tauri/tauri.conf.json", TauriV2);
        Assert.True(MigrateCommand.Run(Request()));

        // The whole point: `carbon dev` must accept what migrate produced.
        var config = DotCarbon.Core.Config.ConfigLoader.Load(Path.Combine(_root, "carbon.json"));

        Assert.Equal("Notes", config.App.Name);
        Assert.Equal("com.acme.notes", config.App.Identifier);
        Assert.Equal("http://localhost:1420", config.Build.DevUrl);
        Assert.Equal("dist", config.Build.FrontendDist);
    }

    private MigrateCommand.MigrateRequest Request() =>
        new(_root, From: null, CarbonVersion: "0.7.0", Force: false, DryRun: false);

    private void WriteFile(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class RecordingMigration(int fromVersion, List<int> log) : IConfigMigration
    {
        public int FromVersion => fromVersion;

        public string Describe => $"v{fromVersion} -> v{fromVersion + 1}";

        public void Apply(JsonObject config) => log.Add(fromVersion);
    }

    private const string TauriV2 = """
        {
          "$schema": "https://schema.tauri.app/config/2",
          "productName": "Notes",
          "version": "2.3.1",
          "identifier": "com.acme.notes",
          "build": {
            "beforeDevCommand": "npm run dev",
            "devUrl": "http://localhost:1420",
            "beforeBuildCommand": "npm run build",
            "frontendDist": "../dist"
          },
          "app": {
            "windows": [
              { "title": "Notes", "width": 1024, "height": 720, "minWidth": 600, "resizable": true }
            ],
            "security": { "csp": "default-src 'self'" }
          },
          "bundle": { "category": "Productivity", "icon": ["icons/icon.png"] },
          "plugins": { "shell": { "open": true }, "fs": { "scope": ["$APPDATA/*"] } }
        }
        """;

    private const string TauriV1 = """
        {
          "package": { "productName": "OldApp", "version": "1.5.0" },
          "build": { "beforeDevCommand": "yarn dev", "devPath": "http://localhost:8080", "distDir": "../public" },
          "tauri": {
            "bundle": { "identifier": "io.legacy.oldapp", "category": "Utility", "icon": ["icon.png"] },
            "windows": [ { "title": "Old App", "width": 640, "height": 480, "resizable": false } ],
            "security": { "csp": "default-src 'self'" },
            "allowlist": { "fs": { "all": true } }
          }
        }
        """;
}
