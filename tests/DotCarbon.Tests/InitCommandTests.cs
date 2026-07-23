using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.1: `carbon init` adopts Carbon into an existing frontend project. The detection half is
/// pure (it takes file contents), so every framework case is covered here; the scaffolding half is
/// driven against real temp directories.
/// </summary>
public class InitCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carbon-init-{Guid.NewGuid():N}");

    public InitCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    // The first four ship Vite themselves, so these rows also prove a meta-framework beats the Vite
    // it depends on — matching Vite first would mislabel every one of them.
    [InlineData("next,vite", "Next.js", "http://localhost:3000", "out")]
    [InlineData("nuxt,vite", "Nuxt", "http://localhost:3000", ".output/public")]
    [InlineData("@sveltejs/kit,vite", "SvelteKit", "http://localhost:5173", "build")]
    [InlineData("astro,vite", "Astro", "http://localhost:4321", "dist")]
    [InlineData("react-scripts", "Create React App", "http://localhost:3000", "build")]
    [InlineData("parcel", "Parcel", "http://localhost:1234", "dist")]
    [InlineData("webpack-dev-server", "webpack", "http://localhost:8080", "dist")]
    public void Detects_each_framework_dev_url_and_output_dir(
        string dependencies, string framework, string devUrl, string dist)
    {
        var plan = FrontendDetector.Detect(
            PackageJson("app", dependencies.Split(',')), ["package-lock.json"]);

        Assert.Equal(framework, plan.Framework);
        Assert.Equal(devUrl, plan.DevUrl);
        Assert.Equal(dist, plan.Dist);
    }

    [Fact]
    public void Plain_vite_is_detected_when_no_meta_framework_is_present()
    {
        var plan = FrontendDetector.Detect(PackageJson("app", dependencies: ["vite", "react"]), []);

        Assert.Equal("Vite", plan.Framework);
        Assert.Equal("http://localhost:5173", plan.DevUrl);
        Assert.Equal("dist", plan.Dist);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Angular_output_is_scoped_to_the_project_name()
    {
        var plan = FrontendDetector.Detect(PackageJson("dashboard", dependencies: ["@angular/core"]), []);

        Assert.Equal("dist/dashboard/browser", plan.Dist);
        Assert.Equal("http://localhost:4200", plan.DevUrl);
    }

    [Fact]
    public void An_explicit_port_in_the_framework_config_beats_the_default()
    {
        // A project that moved its dev server would otherwise get a carbon.json pointing at nothing.
        var plan = FrontendDetector.Detect(
            PackageJson("app", dependencies: ["vite"]), ["vite.config.ts"],
            "export default defineConfig({ server: { port: 4000, strictPort: true } })");

        Assert.Equal("http://localhost:4000", plan.DevUrl);
    }

    [Theory]
    [InlineData("pnpm-lock.yaml", "pnpm")]
    [InlineData("yarn.lock", "yarn")]
    [InlineData("bun.lockb", "bun")]
    [InlineData("package-lock.json", "npm")]
    public void The_lockfile_picks_the_package_manager(string lockfile, string expected)
    {
        var plan = FrontendDetector.Detect(PackageJson("app", dependencies: ["vite"]), [lockfile]);

        Assert.Equal(expected, plan.PackageManager);
    }

    [Fact]
    public void Server_rendered_frameworks_warn_that_carbon_serves_static_files()
    {
        // Silently writing `out` for a Next app that never runs `output: "export"` would produce a
        // config that looks right and fails at bundle time.
        var plan = FrontendDetector.Detect(PackageJson("app", dependencies: ["next"]), []);

        Assert.Contains(plan.Warnings, warning => warning.Contains("output: \"export\""));
    }

    [Fact]
    public void Unknown_tooling_is_reported_rather_than_guessed_silently()
    {
        var plan = FrontendDetector.Detect(PackageJson("app", dependencies: ["lodash"]), []);

        Assert.Equal("unknown", plan.Framework);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void Malformed_package_json_still_yields_a_usable_plan()
    {
        var plan = FrontendDetector.Detect("{ not json", []);

        Assert.Equal("unknown", plan.Framework);
        Assert.Null(plan.DevScript);
    }

    [Fact]
    public void Init_writes_a_config_and_the_csharp_side_for_a_root_level_frontend()
    {
        WriteFrontend(_root, "my-app", ["vite"]);

        Assert.True(InitCommand.Run(Request()));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal("Myapp", config["app"]!["name"]!.GetValue<string>());
        Assert.Equal("com.example.myapp", config["app"]!["identifier"]!.GetValue<string>());
        Assert.Equal("http://localhost:5173", config["build"]!["devUrl"]!.GetValue<string>());
        Assert.Equal("dist", config["build"]!["frontendDist"]!.GetValue<string>());
        Assert.Equal("npm run dev", config["build"]!["devCommand"]!.GetValue<string>());
        Assert.Equal("src-carbon", config["build"]!["backendProject"]!.GetValue<string>());

        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "Myapp.csproj")));
        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "Program.cs")));
        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "capabilities", "main.json")));
        // The mobile generators reference ..\..\..\src-shared\AppLogic.csproj by name, so the shared
        // library has to land at exactly this path for `carbon platform add` to work later.
        Assert.True(File.Exists(Path.Combine(_root, "src-shared", "AppLogic.csproj")));
        Assert.True(File.Exists(Path.Combine(_root, "src-shared", "AppCommands.cs")));
    }

    [Fact]
    public void A_frontend_in_a_subdirectory_keeps_its_prefix_in_frontend_dist()
    {
        WriteFrontend(Path.Combine(_root, "web"), "site", ["vite"]);

        Assert.True(InitCommand.Run(Request()));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal("web/dist", config["build"]!["frontendDist"]!.GetValue<string>());
    }

    [Fact]
    public void Several_candidate_frontends_are_a_question_for_the_user_not_a_guess()
    {
        WriteFrontend(Path.Combine(_root, "web"), "web", ["vite"]);
        WriteFrontend(Path.Combine(_root, "admin"), "admin", ["vite"]);

        Assert.False(InitCommand.Run(Request()));
        Assert.False(File.Exists(Path.Combine(_root, "carbon.json")));
    }

    [Fact]
    public void An_explicit_frontend_directory_resolves_the_ambiguity()
    {
        WriteFrontend(Path.Combine(_root, "web"), "web", ["vite"]);
        WriteFrontend(Path.Combine(_root, "admin"), "admin", ["vite"]);

        Assert.True(InitCommand.Run(Request() with { FrontendDir = Path.Combine(_root, "admin") }));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal("admin/dist", config["build"]!["frontendDist"]!.GetValue<string>());
    }

    [Fact]
    public void An_existing_carbon_project_is_not_overwritten_without_force()
    {
        WriteFrontend(_root, "app", ["vite"]);
        File.WriteAllText(Path.Combine(_root, "carbon.json"), "{ \"app\": { \"name\": \"Existing\" } }");

        Assert.False(InitCommand.Run(Request()));
        Assert.Contains("Existing", File.ReadAllText(Path.Combine(_root, "carbon.json")));

        Assert.True(InitCommand.Run(Request() with { Force = true }));
        Assert.DoesNotContain("Existing", File.ReadAllText(Path.Combine(_root, "carbon.json")));
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        WriteFrontend(_root, "app", ["vite"]);

        Assert.True(InitCommand.Run(Request() with { DryRun = true }));

        Assert.False(File.Exists(Path.Combine(_root, "carbon.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "src-carbon")));
    }

    [Fact]
    public void A_frontend_with_no_dev_script_fails_instead_of_writing_a_config_that_cannot_run()
    {
        File.WriteAllText(Path.Combine(_root, "package.json"),
            "{ \"name\": \"app\", \"scripts\": { \"build\": \"vite build\" }, " +
            "\"devDependencies\": { \"vite\": \"^5\" } }");

        Assert.False(InitCommand.Run(Request()));
        Assert.False(File.Exists(Path.Combine(_root, "carbon.json")));
    }

    [Fact]
    public void Gitignore_gains_the_carbon_outputs_without_duplicating_existing_entries()
    {
        WriteFrontend(_root, "app", ["vite"]);
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "node_modules/\nbin/\n");

        Assert.True(InitCommand.Run(Request()));

        var lines = File.ReadAllLines(Path.Combine(_root, ".gitignore"));
        Assert.Single(lines, line => line.Trim() == "bin/");
        Assert.Contains("obj/", lines);
        Assert.Contains(".carbon/", lines);
    }

    [Fact]
    public void Explicit_options_override_what_was_detected()
    {
        WriteFrontend(_root, "app", ["vite"]);

        Assert.True(InitCommand.Run(Request() with
        {
            Name = "Custom",
            Identifier = "dev.example.custom",
            DevUrl = "http://localhost:9999",
            Dist = "public",
        }));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        Assert.Equal("Custom", config["app"]!["name"]!.GetValue<string>());
        Assert.Equal("dev.example.custom", config["app"]!["identifier"]!.GetValue<string>());
        Assert.Equal("http://localhost:9999", config["build"]!["devUrl"]!.GetValue<string>());
        Assert.Equal("public", config["build"]!["frontendDist"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("my-app", "Myapp")]
    [InlineData("@acme/dashboard", "Dashboard")]
    [InlineData("123app", "App123app")]
    [InlineData("---", "App")]
    public void Package_names_become_valid_assembly_names(string packageName, string expected) =>
        Assert.Equal(expected, InitCommand.Sanitize(packageName));

    [Fact]
    public void The_generated_config_parses_as_a_carbon_config()
    {
        WriteFrontend(_root, "app", ["vite"]);
        Assert.True(InitCommand.Run(Request()));

        // Round-trips through the real loader, so init cannot emit something `carbon dev` rejects.
        var config = DotCarbon.Core.Config.ConfigLoader.Load(Path.Combine(_root, "carbon.json"));

        Assert.Equal("App", config.App.Name);
        Assert.Equal("http://localhost:5173", config.Build.DevUrl);
        Assert.Equal("dist", config.Build.FrontendDist);
        Assert.True(config.Security.Enabled);
    }

    private InitCommand.InitRequest Request() =>
        new(_root, null, null, null, null, null, "0.1.0", Force: false, DryRun: false);

    private static void WriteFrontend(string dir, string name, string[] dependencies)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), PackageJson(name, dependencies));
    }

    private static string PackageJson(string name, string[] dependencies)
    {
        var deps = string.Join(", ", dependencies.Select(dependency => $"\"{dependency}\": \"^1.0.0\""));
        return $$"""
                 {
                   "name": "{{name}}",
                   "scripts": { "dev": "vite", "build": "vite build" },
                   "devDependencies": { {{deps}} }
                 }
                 """;
    }
}
