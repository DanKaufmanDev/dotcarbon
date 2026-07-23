using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon migrate</c> — two migrations behind one command:
/// <list type="bullet">
/// <item>an existing <c>carbon.json</c> is upgraded across schema versions (the version engine);</item>
/// <item>a Tauri project (<c>tauri.conf.json</c>) is imported into a Carbon project.</item>
/// </list>
/// It auto-detects which applies, so <c>carbon migrate</c> does the right thing whether you are moving
/// off Tauri or moving a Carbon app forward.
/// </summary>
public static class MigrateCommand
{
    public static Command Build()
    {
        var command = new Command("migrate", "Upgrade carbon.json, or import an existing Tauri project");

        var project = new Option<DirectoryInfo?>(
            "--project", "Project directory (default: current directory)");
        var from = new Option<string?>("--from", "Force the source: 'tauri' or 'carbon' (default: auto-detect)");
        var version = new Option<string?>(
            "--carbon-version", "DotCarbon package version for the scaffold (default: this CLI's version)");
        var force = new Option<bool>("--force", "Overwrite files that already exist");
        var dryRun = new Option<bool>("--dry-run", "Print what would change without writing it");

        foreach (var option in new Option[] { project, from, version, force, dryRun })
            command.AddOption(option);

        command.SetHandler(context =>
        {
            var parsed = context.ParseResult;
            var request = new MigrateRequest(
                parsed.GetValueForOption(project)?.FullName ?? Directory.GetCurrentDirectory(),
                parsed.GetValueForOption(from),
                parsed.GetValueForOption(version) ?? ProjectScaffold.CarbonVersion(),
                parsed.GetValueForOption(force),
                parsed.GetValueForOption(dryRun));

            context.ExitCode = Run(request) ? 0 : 1;
        });

        return command;
    }

    internal sealed record MigrateRequest(
        string ProjectDir, string? From, string CarbonVersion, bool Force, bool DryRun);

    internal enum Source { Carbon, Tauri }

    internal static bool Run(MigrateRequest request)
    {
        if (!TryResolveSource(request, out var source, out var error))
        {
            Error(error);
            return false;
        }

        return source == Source.Tauri ? ImportTauri(request) : UpgradeCarbon(request);
    }

    /// <summary>
    /// `--from` wins; otherwise a `carbon.json` means "upgrade" and a `tauri.conf.json` means "import".
    /// Both present is ambiguous, so it asks the user to disambiguate rather than pick.
    /// </summary>
    private static bool TryResolveSource(MigrateRequest request, out Source source, out string error)
    {
        source = Source.Carbon;
        error = string.Empty;

        if (request.From is not null)
        {
            switch (request.From.Trim().ToLowerInvariant())
            {
                case "tauri": source = Source.Tauri; return true;
                case "carbon": source = Source.Carbon; return true;
                default:
                    error = $"Unknown --from '{request.From}'. Use 'tauri' or 'carbon'.";
                    return false;
            }
        }

        var hasCarbon = File.Exists(Path.Combine(request.ProjectDir, "carbon.json"));
        var hasTauri = FindTauriConfig(request.ProjectDir) is not null;

        if (hasCarbon && hasTauri)
        {
            error = "Found both carbon.json and tauri.conf.json — pass --from tauri or --from carbon.";
            return false;
        }

        if (hasCarbon) { source = Source.Carbon; return true; }
        if (hasTauri) { source = Source.Tauri; return true; }

        error = "No carbon.json or tauri.conf.json found here. Run this in a project directory, " +
                "or use `carbon init` for a project with no config yet.";
        return false;
    }

    // ---- carbon.json version upgrade ----------------------------------------------------------

    private static bool UpgradeCarbon(MigrateRequest request)
    {
        var path = Path.Combine(request.ProjectDir, "carbon.json");
        JsonObject config;
        try
        {
            config = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new JsonException("carbon.json is not a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Error($"Could not read carbon.json: {ex.Message}");
            return false;
        }

        var outcome = ConfigMigrationEngine.Migrate(config);
        if (!outcome.Changed)
        {
            Info($"carbon.json is already at the current schema version ({outcome.ToVersion}). Nothing to do.");
            return true;
        }

        Info($"Upgrading carbon.json schema: v{outcome.FromVersion} -> v{outcome.ToVersion}");
        foreach (var step in outcome.Applied) Success($"  {step}");

        if (request.DryRun)
        {
            Info("Dry run — carbon.json not written.");
            return true;
        }

        File.WriteAllText(path, Serialize(config));
        Success($"wrote {Path.GetFileName(path)}");
        return true;
    }

    // ---- Tauri import -------------------------------------------------------------------------

    private static bool ImportTauri(MigrateRequest request)
    {
        var root = request.ProjectDir;
        var configPath = Path.Combine(root, "carbon.json");
        if (File.Exists(configPath) && !request.Force)
        {
            Error("carbon.json already exists — pass --force to overwrite it.");
            return false;
        }

        var tauriPath = FindTauriConfig(root);
        if (tauriPath is null)
        {
            Error("No tauri.conf.json found (looked in ./ and ./src-tauri/).");
            return false;
        }

        TauriImport import;
        try
        {
            import = TauriConfigImporter.Import(File.ReadAllText(tauriPath), new DirectoryInfo(root).Name);
        }
        catch (JsonException ex)
        {
            Error($"Could not parse {Path.GetRelativePath(root, tauriPath)}: {ex.Message}");
            return false;
        }

        var appName = ProjectScaffold.SanitizeAppName(import.AppName);
        Info($"Importing Tauri v{import.TauriVersion} project: {import.AppName} ({import.Identifier})");
        Info($"Source: {Path.GetRelativePath(root, tauriPath)}");
        if (import.DevUrl is not null) Info($"Dev URL: {import.DevUrl}");

        var files = new List<(string Path, string Content)> { (configPath, Serialize(import.Carbon)) };
        files.AddRange(ProjectScaffold.CSharpFiles(root, appName, request.CarbonVersion));

        foreach (var (path, content) in files)
        {
            var relative = Path.GetRelativePath(root, path);
            if (File.Exists(path) && !request.Force)
            {
                Warn($"skipped {relative} (already exists — pass --force to overwrite)");
                continue;
            }

            if (request.DryRun)
            {
                Info($"would write {relative}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            Success($"wrote {relative}");
        }

        if (!request.DryRun) UpdateGitignore(root);

        Console.WriteLine();
        WriteColor("[Carbon] Review before running — these did not translate automatically:", ConsoleColor.Yellow);
        foreach (var warning in import.Warnings) Warn($"  • {warning}");

        Console.WriteLine();
        Console.WriteLine("[Carbon] Next steps:");
        Console.WriteLine("  1. Port your Rust commands to C# in src-shared/AppCommands.cs");
        Console.WriteLine("  2. Run the app:  carbon dev");
        Console.WriteLine("  3. You can remove src-tauri/ once the C# backend covers what you need.");
        return true;
    }

    private static string? FindTauriConfig(string root)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(root, "src-tauri", "tauri.conf.json"),
            Path.Combine(root, "tauri.conf.json"),
        })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static void UpdateGitignore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        var existing = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var missing = new[] { "bin/", "obj/", ".carbon/" }
            .Where(entry => !existing.Any(line => line.Trim() == entry))
            .ToList();
        if (missing.Count == 0) return;

        var addition = (existing.Count > 0 && !string.IsNullOrWhiteSpace(existing[^1]) ? "\n" : string.Empty) +
                       "# Carbon\n" + string.Join("\n", missing) + "\n";
        File.AppendAllText(path, addition);
        Success($"updated {Path.GetFileName(path)}");
    }

    private static string Serialize(JsonNode node) =>
        node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

    private static void Info(string message) => Console.WriteLine($"[Carbon] {message}");

    private static void Success(string message) => WriteColor($"[Carbon] {message}", ConsoleColor.Green);

    private static void Warn(string message) => WriteColor($"[Carbon] {message}", ConsoleColor.Yellow);

    private static void Error(string message) => WriteColor($"[Carbon] {message}", ConsoleColor.Red);

    private static void WriteColor(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
