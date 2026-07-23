using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotCarbon.Cli.Commands;

/// <summary>What `carbon init` inferred about an existing frontend project.</summary>
internal sealed record FrontendPlan(
    string Framework,
    string PackageManager,
    string? DevScript,
    string? BuildScript,
    string DevUrl,
    string Dist,
    string? AppName,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Infers the dev URL, build output directory and scripts of an existing frontend, so `carbon init`
/// can write a <c>carbon.json</c> that works without the user hand-editing it. Pure by design (it
/// takes file *contents*, not paths) so every framework case is unit-testable.
/// </summary>
internal static partial class FrontendDetector
{
    /// <param name="packageJson">Contents of the frontend's package.json.</param>
    /// <param name="presentFiles">File names in the frontend directory (lockfiles, vite config, …).</param>
    /// <param name="configText">
    /// Contents of the framework config (vite.config.*, next.config.*, …) when one exists — read for
    /// an explicit dev-server port, which overrides the framework default.
    /// </param>
    public static FrontendPlan Detect(
        string packageJson,
        IReadOnlyCollection<string> presentFiles,
        string? configText = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(packageJson); }
        catch (JsonException) { root = null; }

        var dependencies = ReadDependencies(root);
        var scripts = ReadScripts(root);
        var name = (root?["name"] as JsonValue)?.GetValue<string>();

        var warnings = new List<string>();
        var (framework, port, dist) = Match(dependencies, name, warnings);

        // An explicit port in the framework config beats the framework's default.
        if (configText is not null && PortPattern().Match(configText) is { Success: true } match)
            port = int.Parse(match.Groups[1].Value);

        return new FrontendPlan(
            framework,
            DetectPackageManager(presentFiles),
            PickScript(scripts, "dev", "start", "serve"),
            PickScript(scripts, "build", "generate"),
            $"http://localhost:{port}",
            dist,
            name,
            warnings);
    }

    /// <summary>
    /// Framework → (dev port, static output dir). Ordered most-specific first: the meta-frameworks all
    /// depend on Vite, so matching Vite first would mislabel every one of them.
    /// </summary>
    private static (string Framework, int Port, string Dist) Match(
        IReadOnlyDictionary<string, string> dependencies, string? name, List<string> warnings)
    {
        if (dependencies.ContainsKey("next"))
        {
            warnings.Add("Next.js: Carbon serves static files, so set `output: \"export\"` in next.config " +
                         "(server-rendered routes and API routes will not work).");
            return ("Next.js", 3000, "out");
        }

        if (dependencies.ContainsKey("nuxt"))
        {
            warnings.Add("Nuxt: build with `nuxt generate` (static) — the default `nuxt build` output " +
                         "needs a Node server Carbon does not run.");
            return ("Nuxt", 3000, ".output/public");
        }

        if (dependencies.ContainsKey("@sveltejs/kit"))
        {
            warnings.Add("SvelteKit: use adapter-static, otherwise the build output needs a Node server.");
            return ("SvelteKit", 5173, "build");
        }

        if (dependencies.ContainsKey("@remix-run/dev") || dependencies.ContainsKey("@react-router/dev"))
        {
            warnings.Add("Remix/React Router: Carbon serves static files, so this needs an SPA/prerender " +
                         "configuration rather than the default server build.");
            return ("Remix", 3000, "build/client");
        }

        if (dependencies.ContainsKey("astro")) return ("Astro", 4321, "dist");

        if (dependencies.ContainsKey("@angular/core"))
        {
            // Angular 17+ nests the browser build; older versions emit straight into dist/<name>.
            var project = string.IsNullOrWhiteSpace(name) ? "app" : name;
            return ("Angular", 4200, $"dist/{project}/browser");
        }

        if (dependencies.ContainsKey("react-scripts")) return ("Create React App", 3000, "build");
        if (dependencies.ContainsKey("vite")) return ("Vite", 5173, "dist");
        if (dependencies.ContainsKey("parcel")) return ("Parcel", 1234, "dist");
        if (dependencies.ContainsKey("webpack-dev-server") || dependencies.ContainsKey("webpack"))
            return ("webpack", 8080, "dist");

        warnings.Add("Could not identify the frontend tooling — check build.devUrl and build.frontendDist " +
                     "in carbon.json against what your dev server and build actually use.");
        return ("unknown", 5173, "dist");
    }

    /// <summary>The lockfile is the only reliable signal of which package manager the project uses.</summary>
    private static string DetectPackageManager(IReadOnlyCollection<string> presentFiles)
    {
        var files = new HashSet<string>(presentFiles, StringComparer.OrdinalIgnoreCase);
        if (files.Contains("pnpm-lock.yaml")) return "pnpm";
        if (files.Contains("yarn.lock")) return "yarn";
        if (files.Contains("bun.lockb") || files.Contains("bun.lock")) return "bun";
        return "npm";
    }

    private static string? PickScript(IReadOnlyDictionary<string, string> scripts, params string[] candidates) =>
        candidates.FirstOrDefault(scripts.ContainsKey);

    private static IReadOnlyDictionary<string, string> ReadDependencies(JsonNode? root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (root?[section] is not JsonObject dependencies) continue;
            foreach (var entry in dependencies)
                result[entry.Key] = entry.Value?.ToString() ?? string.Empty;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadScripts(JsonNode? root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root?["scripts"] is not JsonObject scripts) return result;
        foreach (var entry in scripts)
            result[entry.Key] = entry.Value?.ToString() ?? string.Empty;
        return result;
    }

    [GeneratedRegex(@"port\s*:\s*(\d{2,5})")]
    private static partial Regex PortPattern();
}
