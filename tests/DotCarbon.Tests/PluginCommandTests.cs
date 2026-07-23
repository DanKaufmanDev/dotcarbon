using System.Text.Json.Nodes;
using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.2: `carbon plugin new` scaffolds a third-party plugin. That the output actually *works* is
/// verified by building and consuming a scaffolded plugin end-to-end; these tests pin the naming
/// rules, the file set and the guardrails.
/// </summary>
public class PluginCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carbon-plugin-{Guid.NewGuid():N}");

    public PluginCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("confetti", "confetti")]
    [InlineData("acme-confetti", "acme-confetti")]
    [InlineData("AcmeConfetti", "acme-confetti")]
    [InlineData("acme_confetti", "acme-confetti")]
    [InlineData("Acme Confetti", "acme-confetti")]
    [InlineData("--acme--", "acme")]
    public void Names_normalize_to_kebab_case(string input, string expected) =>
        Assert.Equal(expected, PluginCommand.Kebab(input));

    [Fact]
    public void Naming_is_consistent_across_csharp_npm_and_the_bridge()
    {
        var names = PluginCommand.Resolve(Request("acme-confetti"));

        Assert.Equal("AcmeConfetti", names.Pascal);
        Assert.Equal("AcmeConfettiPlugin", names.ClassName);
        Assert.Equal("acme-confetti", names.Namespace);
        Assert.Equal("carbon-plugin-acme-confetti", names.NpmPackage);
        // Not DotCarbon.* — that prefix belongs to the first-party packages.
        Assert.Equal("CarbonPlugin.AcmeConfetti", names.ProjectId);
    }

    [Fact]
    public void Identifiers_can_be_overridden_independently()
    {
        var names = PluginCommand.Resolve(Request("confetti") with
        {
            Namespace = "party",
            Id = "Acme.Confetti",
            NpmPackage = "@acme/confetti",
        });

        Assert.Equal("party", names.Namespace);
        Assert.Equal("Acme.Confetti", names.ProjectId);
        Assert.Equal("@acme/confetti", names.NpmPackage);
    }

    [Fact]
    public void Scaffolds_the_project_types_json_context_permissions_tests_and_js_package()
    {
        Assert.True(PluginCommand.Run(Request("confetti")));

        Assert.True(File.Exists(Path.Combine(_root, "CarbonPlugin.Confetti", "CarbonPlugin.Confetti.csproj")));
        Assert.True(File.Exists(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiPlugin.cs")));
        Assert.True(File.Exists(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiTypes.cs")));
        Assert.True(File.Exists(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiJsonContext.cs")));
        Assert.True(File.Exists(Path.Combine(_root, "permissions", "confetti.json")));
        Assert.True(File.Exists(Path.Combine(_root, "tests", "CarbonPlugin.Confetti.Tests",
            "CarbonPlugin.Confetti.Tests.csproj")));
        Assert.True(File.Exists(Path.Combine(_root, "js", "src", "index.ts")));
        Assert.True(File.Exists(Path.Combine(_root, "README.md")));
    }

    [Fact]
    public void The_plugin_class_carries_what_the_generator_and_the_acl_require()
    {
        Assert.True(PluginCommand.Run(Request("confetti")));

        var source = File.ReadAllText(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiPlugin.cs"));

        // Without `partial` the source generator reports an error instead of emitting the registration,
        // and without the permission attribute the plugin cannot be granted in a capability.
        Assert.Contains("public partial class ConfettiPlugin : IPlugin", source);
        Assert.Contains("[CarbonPermission(\"confetti:default\"", source);
        Assert.Contains("[CarbonCommand(\"ping\")]", source);
        Assert.Contains("public string Namespace => \"confetti\";", source);
    }

    [Fact]
    public void The_json_context_covers_every_scaffolded_argument_and_result_type()
    {
        // A missing [JsonSerializable] only fails once a command is called from a trimmed/AOT app,
        // which is exactly the kind of late failure the scaffold should prevent.
        Assert.True(PluginCommand.Run(Request("confetti")));

        var source = File.ReadAllText(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiJsonContext.cs"));

        Assert.Contains("[JsonSerializable(typeof(ConfettiArgs))]", source);
        Assert.Contains("[JsonSerializable(typeof(ConfettiResult))]", source);
        Assert.Contains("JsonKnownNamingPolicy.CamelCase", source);
    }

    [Fact]
    public void The_permission_manifest_matches_what_carbon_capabilities_discovers()
    {
        Assert.True(PluginCommand.Run(Request("confetti")));

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "permissions", "confetti.json")))!;

        Assert.Equal("confetti", manifest["namespace"]!.GetValue<string>());
        Assert.Equal("CarbonPlugin.Confetti", manifest["package"]!.GetValue<string>());
        var permission = manifest["permissions"]!.AsArray()[0]!;
        Assert.Equal("confetti:default", permission["identifier"]!.GetValue<string>());
        Assert.Equal(
            ["confetti:ping", "confetti:echo"],
            permission["commands"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void Custom_commands_flow_through_csharp_the_manifest_and_the_js_package()
    {
        Assert.True(PluginCommand.Run(Request("confetti") with { Commands = ["burst", "clear"] }));

        var source = File.ReadAllText(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiPlugin.cs"));
        Assert.Contains("[CarbonCommand(\"burst\")]", source);
        Assert.Contains("[CarbonCommand(\"clear\")]", source);
        Assert.DoesNotContain("[CarbonCommand(\"ping\")]", source);

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "permissions", "confetti.json")))!;
        Assert.Equal(
            ["confetti:burst", "confetti:clear"],
            manifest["permissions"]![0]!["commands"]!.AsArray().Select(node => node!.GetValue<string>()));

        var index = File.ReadAllText(Path.Combine(_root, "js", "src", "index.ts"));
        Assert.Contains("export const burst", index);
        Assert.Contains("'confetti:clear': { args: void; result: string }", index);
    }

    [Fact]
    public void The_js_package_augments_the_bridge_command_map()
    {
        // Without the module augmentation, consumers get an untyped `invoke` for these commands.
        Assert.True(PluginCommand.Run(Request("confetti")));

        var index = File.ReadAllText(Path.Combine(_root, "js", "src", "index.ts"));

        Assert.Contains("declare module '@dotcarbon/api'", index);
        Assert.Contains("interface CarbonCommands", index);
        Assert.Contains("'confetti:echo': { args: { message: string }; result: ConfettiResult }", index);
    }

    [Fact]
    public void Optional_pieces_can_be_skipped()
    {
        Assert.True(PluginCommand.Run(Request("confetti") with { IncludeJs = false, IncludeTests = false }));

        Assert.False(Directory.Exists(Path.Combine(_root, "js")));
        Assert.False(Directory.Exists(Path.Combine(_root, "tests")));
        Assert.True(File.Exists(Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiPlugin.cs")));

        // InternalsVisibleTo only makes sense when there is a test project to see them.
        var csproj = File.ReadAllText(Path.Combine(_root, "CarbonPlugin.Confetti", "CarbonPlugin.Confetti.csproj"));
        Assert.DoesNotContain("InternalsVisibleTo", csproj);
    }

    [Fact]
    public void Existing_files_are_kept_unless_force_is_passed()
    {
        Assert.True(PluginCommand.Run(Request("confetti")));
        var path = Path.Combine(_root, "CarbonPlugin.Confetti", "ConfettiPlugin.cs");
        File.WriteAllText(path, "// my edits");

        Assert.True(PluginCommand.Run(Request("confetti")));
        Assert.Equal("// my edits", File.ReadAllText(path));

        Assert.True(PluginCommand.Run(Request("confetti") with { Force = true }));
        Assert.Contains("CarbonCommand", File.ReadAllText(path));
    }

    [Fact]
    public void Dry_run_writes_nothing()
    {
        Assert.True(PluginCommand.Run(Request("confetti") with { DryRun = true }));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("---")]
    public void A_name_with_nothing_usable_in_it_is_rejected(string name)
    {
        Assert.False(PluginCommand.Run(Request(name)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    private PluginCommand.PluginRequest Request(string name) =>
        new(name, _root, null, null, null, ["ping", "echo"], "0.7.0",
            IncludeJs: true, IncludeTests: true, Force: false, DryRun: false);
}
