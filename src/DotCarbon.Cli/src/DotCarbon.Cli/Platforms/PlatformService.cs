using System.Reflection;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Platforms;

/// <summary>
/// Owns the <c>.carbon/platforms/&lt;id&gt;</c> generated shells: add, sync (regenerate while
/// preserving manual edits), and list (report drift). Reproducible generation + a manifest of
/// content hashes is what makes "customization survives regeneration" work.
/// </summary>
internal static class PlatformService
{
    public static readonly string[] KnownIds = ["android", "ios", "desktop"];

    public static IPlatformGenerator? GeneratorFor(string id) => id.ToLowerInvariant() switch
    {
        "android" => new AndroidPlatformGenerator(),
        "ios" => new IosPlatformGenerator(),
        "desktop" => new DesktopPlatformGenerator(),
        _ => null,
    };

    public static string PlatformsRoot(string workingDir) => Path.Combine(workingDir, ".carbon", "platforms");
    public static string PlatformDir(string workingDir, string id) => Path.Combine(PlatformsRoot(workingDir), id);

    public static int Add(CarbonConfig config, string workingDir, string id)
    {
        var generator = GeneratorFor(id);
        if (generator is null) return UnknownPlatform(id);

        var dir = PlatformDir(workingDir, id);
        if (Directory.Exists(dir) && PlatformManifest.Load(dir) is not null)
        {
            Error($"Platform '{id}' already exists at {Rel(workingDir, dir)}. Use `carbon platform sync {id}`.");
            return 1;
        }

        var files = generator.Generate(new PlatformContext(config, workingDir, dir));
        Directory.CreateDirectory(dir);

        var manifest = new PlatformManifest
        {
            Platform = id,
            CarbonVersion = CarbonVersion(),
            GeneratedAt = Now(),
            ConfigHash = PlatformManifest.HashSignature(generator.ConfigSignature(config)),
        };
        foreach (var file in files)
        {
            WriteFile(dir, file);
            if (file.Managed) manifest.Files[Normalize(file.RelativePath)] = PlatformManifest.HashContent(file.Content);
        }
        manifest.Save(dir);

        Success($"Generated {generator.DisplayName} shell → {Rel(workingDir, dir)} ({files.Count} files).");
        Console.WriteLine($"  Managed files regenerate on `carbon platform sync {id}`; other files are yours to edit.");
        return 0;
    }

    public static int Sync(CarbonConfig config, string workingDir, string id, bool force)
    {
        var generator = GeneratorFor(id);
        if (generator is null) return UnknownPlatform(id);

        var dir = PlatformDir(workingDir, id);
        var manifest = Directory.Exists(dir) ? PlatformManifest.Load(dir) : null;
        if (manifest is null)
        {
            Error($"Platform '{id}' is not added. Run `carbon platform add {id}` first.");
            return 1;
        }

        var files = generator.Generate(new PlatformContext(config, workingDir, dir));
        var generatedManaged = files.Where(file => file.Managed)
            .Select(file => Normalize(file.RelativePath))
            .ToHashSet();

        var newFiles = new Dictionary<string, string>();
        var edited = new List<string>();
        int regenerated = 0, created = 0, removed = 0;

        foreach (var file in files)
        {
            var rel = Normalize(file.RelativePath);
            var path = FullPath(dir, file.RelativePath);

            if (!file.Managed)
            {
                if (!File.Exists(path)) { WriteFile(dir, file); created++; }
                continue;
            }

            if (File.Exists(path) && manifest.Files.TryGetValue(rel, out var recorded))
            {
                var onDisk = PlatformManifest.HashContent(File.ReadAllText(path));
                if (onDisk != recorded && !force)
                {
                    edited.Add(rel);
                    newFiles[rel] = recorded; // keep the edited file and its baseline
                    continue;
                }
            }

            var existed = File.Exists(path);
            WriteFile(dir, file);
            newFiles[rel] = PlatformManifest.HashContent(file.Content);
            if (existed) regenerated++; else created++;
        }

        // Managed files no longer generated: delete if untouched, otherwise leave the user's copy.
        foreach (var (rel, recorded) in manifest.Files)
        {
            if (generatedManaged.Contains(rel)) continue;
            var path = FullPath(dir, rel);
            if (!File.Exists(path)) continue;
            if (PlatformManifest.HashContent(File.ReadAllText(path)) == recorded) { File.Delete(path); removed++; }
            else newFiles[rel] = recorded;
        }

        manifest.Files = newFiles;
        manifest.ConfigHash = PlatformManifest.HashSignature(generator.ConfigSignature(config));
        manifest.CarbonVersion = CarbonVersion();
        manifest.GeneratedAt = Now();
        manifest.Save(dir);

        Success($"Synced {generator.DisplayName}: {regenerated} regenerated, {created} new, {removed} removed.");
        if (edited.Count > 0)
        {
            Warn($"Preserved {edited.Count} manually edited file(s) — pass --force to overwrite:");
            foreach (var rel in edited) Console.WriteLine($"    {rel}");
        }
        return 0;
    }

    public static int List(CarbonConfig config, string workingDir)
    {
        var root = PlatformsRoot(workingDir);
        var dirs = Directory.Exists(root)
            ? Directory.GetDirectories(root).OrderBy(dir => dir).ToList()
            : [];
        if (dirs.Count == 0)
        {
            Console.WriteLine("No platforms added. Try `carbon platform add android`.");
            return 0;
        }

        Console.WriteLine("Platforms:");
        foreach (var dir in dirs)
        {
            var id = Path.GetFileName(dir);
            var manifest = PlatformManifest.Load(dir);
            if (manifest is null) { PrintStatus(id, "no manifest", ConsoleColor.DarkGray, ""); continue; }

            var generator = GeneratorFor(id);
            var edited = manifest.Files.Count(kv => IsEdited(dir, kv.Key, kv.Value));

            string status;
            ConsoleColor color;
            if (edited > 0) { status = $"manually edited ({edited} file(s))"; color = ConsoleColor.Yellow; }
            else if (generator is not null &&
                     manifest.ConfigHash != PlatformManifest.HashSignature(generator.ConfigSignature(config)))
            { status = "needs sync (config changed)"; color = ConsoleColor.Yellow; }
            else { status = "up to date"; color = ConsoleColor.Green; }

            PrintStatus(id, status, color, manifest.CarbonVersion);
        }
        return 0;
    }

    private static bool IsEdited(string dir, string rel, string recorded)
    {
        var path = FullPath(dir, rel);
        return File.Exists(path) && PlatformManifest.HashContent(File.ReadAllText(path)) != recorded;
    }

    private static void WriteFile(string dir, GeneratedFile file)
    {
        var path = FullPath(dir, file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, file.Content);
    }

    private static string FullPath(string dir, string relativePath) =>
        Path.Combine(new[] { dir }.Concat(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());

    private static string Normalize(string relativePath) =>
        string.Join('/', relativePath.Split('/', '\\').Where(part => part.Length > 0));

    private static string Rel(string workingDir, string path) => Path.GetRelativePath(workingDir, path);

    private static string CarbonVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static int UnknownPlatform(string id)
    {
        Error($"Unknown platform '{id}'. Known platforms: {string.Join(", ", KnownIds)}.");
        return 1;
    }

    private static void PrintStatus(string id, string status, ConsoleColor color, string version)
    {
        Console.Write($"  {id,-9} ");
        Console.ForegroundColor = color;
        Console.Write(status);
        Console.ResetColor();
        if (!string.IsNullOrEmpty(version)) Console.Write($"  (carbon {version})");
        Console.WriteLine();
    }

    private static void Error(string message) => Write(message, ConsoleColor.Red);
    private static void Warn(string message) => Write(message, ConsoleColor.Yellow);
    private static void Success(string message) => Write(message, ConsoleColor.Green);

    private static void Write(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
