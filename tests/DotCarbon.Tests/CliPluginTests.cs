using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Cli;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.4: the cli plugin parses the process arguments against a declared schema and exposes the
/// result (Tauri's getMatches). These drive the pure parser over flags, value args, arrays, and
/// subcommands.
/// </summary>
public class CliPluginTests
{
    private static ArgMatches Parse(CliArg[] args, string[] tokens) => CliPlugin.Parse(args, [], tokens);

    [Fact]
    public void Flag_is_true_when_present_and_false_when_absent()
    {
        CliArg[] defs = [new("verbose", Short: "v", Long: "verbose")];

        var present = Parse(defs, ["--verbose"]);
        Assert.True(present.Args["verbose"].Value!.GetValue<bool>());
        Assert.Equal(1, present.Args["verbose"].Occurrences);

        var absent = Parse(defs, []);
        Assert.False(absent.Args["verbose"].Value!.GetValue<bool>());
        Assert.Equal(0, absent.Args["verbose"].Occurrences);
    }

    [Fact]
    public void Short_flag_matches_too()
    {
        CliArg[] defs = [new("verbose", Short: "v", Long: "verbose")];
        Assert.True(Parse(defs, ["-v"]).Args["verbose"].Value!.GetValue<bool>());
    }

    [Fact]
    public void Value_argument_captures_the_next_token_and_the_inline_form()
    {
        CliArg[] defs = [new("config", Long: "config", TakesValue: true)];

        Assert.Equal("a.json", Parse(defs, ["--config", "a.json"]).Args["config"].Value!.GetValue<string>());
        Assert.Equal("b.json", Parse(defs, ["--config=b.json"]).Args["config"].Value!.GetValue<string>());
        Assert.Null(Parse(defs, []).Args["config"].Value); // absent value arg → null
    }

    [Fact]
    public void Multiple_value_argument_collects_an_array()
    {
        CliArg[] defs = [new("file", Short: "f", Long: "file", TakesValue: true, Multiple: true)];

        var matches = Parse(defs, ["-f", "one", "--file", "two"]);
        var values = matches.Args["file"].Value!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal(["one", "two"], values);
        Assert.Equal(2, matches.Args["file"].Occurrences);
    }

    [Fact]
    public void Subcommand_consumes_the_rest_and_parses_its_own_args()
    {
        CliArg[] rootArgs = [new("verbose", Long: "verbose")];
        CliSubcommand[] subs = [new("serve", Args: [new("port", Long: "port", TakesValue: true)])];

        var matches = CliPlugin.Parse(rootArgs, subs, ["--verbose", "serve", "--port", "8080"]);

        Assert.True(matches.Args["verbose"].Value!.GetValue<bool>());
        Assert.NotNull(matches.Subcommand);
        Assert.Equal("serve", matches.Subcommand!.Name);
        Assert.Equal("8080", matches.Subcommand.Matches.Args["port"].Value!.GetValue<string>());
    }

    [Fact]
    public void Registers_its_command()
    {
        var registry = new FakeRegistry();
        new CliPlugin().Register(registry);
        Assert.Contains("cli:matches", registry.Handlers.Keys);
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<System.Text.Json.JsonElement, Task<JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<System.Text.Json.JsonElement, Task<JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
