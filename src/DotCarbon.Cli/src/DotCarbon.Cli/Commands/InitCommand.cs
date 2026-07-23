using System.CommandLine;
using System.Text;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// Adopts Carbon into an *existing* frontend project: detects the dev server and build output,
/// writes carbon.json, and scaffolds the C# side (`src-carbon` host + `src-shared` command library).
/// create-app covers greenfield; this covers the project you already have.
/// </summary>
public static class InitCommand
{
    public static Command Build()
    {
        var command = new Command("init", "Adopt Carbon into an existing frontend project");

        var projectOption = new Option<DirectoryInfo?>(
            "--project", "Project root to initialize (default: current directory)");
        var frontendOption = new Option<DirectoryInfo?>(
            "--frontend", "Directory containing the frontend package.json (default: auto-detected)");
        var nameOption = new Option<string?>("--name", "App name (default: from package.json)");
        var identifierOption = new Option<string?>(
            "--identifier", "Bundle identifier, e.g. com.example.app (default: com.example.<name>)");
        var devUrlOption = new Option<string?>("--dev-url", "Dev server URL (default: detected)");
        var distOption = new Option<string?>(
            "--dist", "Frontend build output, relative to the project root (default: detected)");
        var versionOption = new Option<string?>(
            "--carbon-version", "DotCarbon package version to reference (default: this CLI's version)");
        var forceOption = new Option<bool>("--force", "Overwrite files that already exist");
        var dryRunOption = new Option<bool>("--dry-run", "Print what would be written without writing it");

        foreach (var option in new Option[]
        {
            projectOption, frontendOption, nameOption, identifierOption,
            devUrlOption, distOption, versionOption, forceOption, dryRunOption,
        })
        {
            command.AddOption(option);
        }

        command.SetHandler(context =>
        {
            var parsed = context.ParseResult;
            var request = new InitRequest(
                parsed.GetValueForOption(projectOption)?.FullName ?? Directory.GetCurrentDirectory(),
                parsed.GetValueForOption(frontendOption)?.FullName,
                parsed.GetValueForOption(nameOption),
                parsed.GetValueForOption(identifierOption),
                parsed.GetValueForOption(devUrlOption),
                parsed.GetValueForOption(distOption),
                parsed.GetValueForOption(versionOption) ?? ProjectScaffold.CarbonVersion(),
                parsed.GetValueForOption(forceOption),
                parsed.GetValueForOption(dryRunOption));

            context.ExitCode = Run(request) ? 0 : 1;
        });

        return command;
    }

    internal sealed record InitRequest(
        string ProjectDir,
        string? FrontendDir,
        string? Name,
        string? Identifier,
        string? DevUrl,
        string? Dist,
        string CarbonVersion,
        bool Force,
        bool DryRun);

    internal static bool Run(InitRequest request)
    {
        var root = request.ProjectDir;
        var configPath = Path.Combine(root, "carbon.json");
        if (File.Exists(configPath) && !request.Force)
        {
            Error($"{Path.GetRelativePath(root, configPath)} already exists — this is already a Carbon project. " +
                  "Pass --force to overwrite it.");
            return false;
        }

        if (!TryLocateFrontend(root, request.FrontendDir, out var frontendDir, out var locateError))
        {
            Error(locateError);
            return false;
        }

        var plan = DetectPlan(frontendDir);
        var appName = Sanitize(request.Name ?? plan.AppName ?? new DirectoryInfo(root).Name);
        var identifier = request.Identifier ?? $"com.example.{appName.ToLowerInvariant()}";
        var devUrl = request.DevUrl ?? plan.DevUrl;

        // frontendDist is relative to the project root, so a frontend in a subdirectory keeps its prefix.
        var frontendRelative = Relative(root, frontendDir);
        var dist = request.Dist ?? Join(frontendRelative, plan.Dist);

        var devCommand = plan.DevScript is null ? null : $"{plan.PackageManager} run {plan.DevScript}";
        var buildCommand = plan.BuildScript is null ? null : $"{plan.PackageManager} run {plan.BuildScript}";
        if (devCommand is null)
        {
            plan.Warnings.ToList().ForEach(warning => Warn(warning));
            Error("No dev/start/serve script found in package.json — Carbon needs one to run `carbon dev`.");
            return false;
        }

        Info($"Frontend:  {(frontendRelative.Length == 0 ? "." : frontendRelative)} ({plan.Framework}, {plan.PackageManager})");
        Info($"Dev URL:   {devUrl}");
        Info($"Dist:      {dist}");
        Info($"App:       {appName} ({identifier})");

        var files = new List<(string Path, string Content)>
        {
            (configPath, CarbonJson(appName, identifier, devCommand, buildCommand, devUrl, dist)),
        };
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

        foreach (var warning in plan.Warnings) Warn(warning);

        Console.WriteLine();
        Console.WriteLine("[Carbon] Next steps:");
        Console.WriteLine($"  1. Install the JS API:  {plan.PackageManager} add @dotcarbon/api" +
                          (frontendRelative.Length == 0 ? string.Empty : $"   (in {frontendRelative})"));
        Console.WriteLine("  2. Run the app:         carbon dev");
        Console.WriteLine("  3. Call C# from JS:     invoke('app:greet', { name: 'world' })");
        return true;
    }

    /// <summary>
    /// The frontend is wherever package.json is: the project root when Carbon is adopted in place, or a
    /// single obvious subdirectory. Anything ambiguous is the user's call, not a guess.
    /// </summary>
    private static bool TryLocateFrontend(string root, string? explicitDir, out string frontendDir, out string error)
    {
        error = string.Empty;

        if (explicitDir is not null)
        {
            frontendDir = explicitDir;
            if (File.Exists(Path.Combine(explicitDir, "package.json"))) return true;
            error = $"No package.json in {explicitDir}.";
            return false;
        }

        if (File.Exists(Path.Combine(root, "package.json")))
        {
            frontendDir = root;
            return true;
        }

        var candidates = Directory.EnumerateDirectories(root)
            .Where(dir => !IsIgnored(Path.GetFileName(dir)))
            .Where(dir => File.Exists(Path.Combine(dir, "package.json")))
            .ToList();

        frontendDir = candidates.Count == 1 ? candidates[0] : string.Empty;
        if (candidates.Count == 1) return true;

        error = candidates.Count == 0
            ? "No package.json found — run this from your frontend project, or pass --frontend <dir>."
            : "Several frontend projects found (" +
              string.Join(", ", candidates.Select(dir => Path.GetFileName(dir))) +
              ") — pick one with --frontend <dir>.";
        return false;
    }

    private static FrontendPlan DetectPlan(string frontendDir)
    {
        var packageJson = File.ReadAllText(Path.Combine(frontendDir, "package.json"));
        var files = Directory.EnumerateFiles(frontendDir).Select(Path.GetFileName).OfType<string>().ToList();

        var configFile = files.FirstOrDefault(file =>
            file.StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("next.config.", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("astro.config.", StringComparison.OrdinalIgnoreCase));
        var configText = configFile is null ? null : File.ReadAllText(Path.Combine(frontendDir, configFile));

        return FrontendDetector.Detect(packageJson, files, configText);
    }

    /// <summary>Appends the Carbon build outputs, leaving whatever the project already ignores alone.</summary>
    private static void UpdateGitignore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        var existing = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var wanted = new[] { "bin/", "obj/", ".carbon/" };
        var missing = wanted.Where(entry => !existing.Any(line => line.Trim() == entry)).ToList();
        if (missing.Count == 0) return;

        var builder = new StringBuilder();
        if (existing.Count > 0 && !string.IsNullOrWhiteSpace(existing[^1])) builder.AppendLine();
        builder.AppendLine("# Carbon");
        foreach (var entry in missing) builder.AppendLine(entry);

        File.AppendAllText(path, builder.ToString());
        Success($"updated {Path.GetFileName(path)}");
    }

    private static string CarbonJson(
        string appName, string identifier, string devCommand, string? buildCommand, string devUrl, string dist)
    {
        var build = new StringBuilder();
        build.Append($"        \"devCommand\": \"{devCommand}\",\n");
        if (buildCommand is not null) build.Append($"        \"buildCommand\": \"{buildCommand}\",\n");
        build.Append($"        \"devUrl\": \"{devUrl}\",\n");
        build.Append($"        \"frontendDist\": \"{dist}\",\n");
        build.Append($"        \"backendProject\": \"{ProjectScaffold.BackendDir}\"");

        return "{\n" +
               "    \"app\": {\n" +
               $"        \"name\": \"{appName}\",\n" +
               "        \"version\": \"0.1.0\",\n" +
               $"        \"identifier\": \"{identifier}\"\n" +
               "    },\n" +
               "    \"window\": {\n" +
               $"        \"title\": \"{appName}\",\n" +
               "        \"width\": 1200,\n" +
               "        \"height\": 800,\n" +
               "        \"resizable\": true,\n" +
               "        \"center\": true,\n" +
               "        \"devtools\": true,\n" +
               "        \"capabilities\": [\"main\"]\n" +
               "    },\n" +
               "    \"security\": {\n" +
               "        \"enabled\": true\n" +
               "    },\n" +
               "    \"build\": {\n" +
               build + "\n" +
               "    }\n" +
               "}\n";
    }

    /// <summary>An assembly name has to be an identifier, and package.json names often are not.</summary>
    internal static string Sanitize(string name) => ProjectScaffold.SanitizeAppName(name);

    private static string Relative(string root, string dir)
    {
        var relative = Path.GetRelativePath(root, dir).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }

    private static string Join(string prefix, string path) =>
        prefix.Length == 0 ? path : $"{prefix}/{path}";

    private static bool IsIgnored(string name) =>
        name is "node_modules" or "bin" or "obj" or "dist" or "build" or "out" || name.StartsWith('.');

    private static void Info(string message) => Console.WriteLine($"[Carbon] {message}");

    private static void Success(string message) => Write(message, ConsoleColor.Green);

    private static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    private static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
