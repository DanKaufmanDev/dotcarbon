using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.5: `carbon completions bash|zsh|fish|pwsh`. The scripts are generated from a model of the
/// command tree, so these tests build a representative tree and assert each shell's script exposes the
/// commands, nested subcommands and options. The bash script's runtime behaviour is verified
/// separately by sourcing it in a real shell.
/// </summary>
public class CompletionCommandTests
{
    // A stand-in for the real tree: a couple of top-level commands, a nested subcommand, and options.
    private static CompletionNode SampleTree() => new(
        "carbon",
        "root",
        ["--help", "--version"],
        [
            new CompletionNode("init", "Adopt Carbon", ["--project", "--force"], []),
            new CompletionNode("bundle", "Package the app", [],
            [
                new CompletionNode("android", "Bundle for Android", ["--aab", "--release"], []),
                new CompletionNode("desktop", "Bundle for desktop", [], []),
            ]),
        ]);

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void Every_shell_script_names_the_top_level_commands(string shell)
    {
        var script = CompletionCommand.Generate(shell, SampleTree());

        Assert.Contains("init", script);
        Assert.Contains("bundle", script);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void Every_shell_script_offers_nested_subcommands(string shell)
    {
        // A completion that stops at the top level is barely worth installing; `bundle android` is the
        // kind of thing users most want completed.
        var script = CompletionCommand.Generate(shell, SampleTree());

        Assert.Contains("android", script);
        Assert.Contains("desktop", script);
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void Every_shell_script_offers_command_options(string shell)
    {
        var script = CompletionCommand.Generate(shell, SampleTree());

        Assert.Contains("project", script);
        Assert.Contains("aab", script);
    }

    [Fact]
    public void The_case_labels_are_the_path_without_the_leading_carbon()
    {
        // The shell functions accumulate the words after `carbon`, so a full-path label would never match.
        Assert.Equal(string.Empty, CompletionCommand.SubPath("carbon"));
        Assert.Equal("bundle", CompletionCommand.SubPath("carbon bundle"));
        Assert.Equal("bundle android", CompletionCommand.SubPath("carbon bundle android"));
    }

    [Fact]
    public void Flatten_visits_every_command_in_the_tree()
    {
        var paths = CompletionCommand.Flatten(SampleTree()).Select(entry => entry.Path).ToList();

        Assert.Equal(
            ["carbon", "carbon init", "carbon bundle", "carbon bundle android", "carbon bundle desktop"],
            paths);
    }

    [Fact]
    public void The_bash_script_does_not_depend_on_the_bash_completion_package()
    {
        // `_init_completion` is not present on stock bash (macOS), which would make the script inert.
        var script = CompletionCommand.Generate("bash", SampleTree());

        Assert.DoesNotContain("_init_completion", script);
        Assert.Contains("COMP_WORDS", script);
        Assert.Contains("complete -F _carbon carbon", script);
    }

    [Fact]
    public void The_zsh_script_declares_the_compdef_tag()
    {
        var script = CompletionCommand.Generate("zsh", SampleTree());

        Assert.StartsWith("#compdef carbon", script);
    }

    [Fact]
    public void The_pwsh_script_registers_a_native_argument_completer()
    {
        var script = CompletionCommand.Generate("pwsh", SampleTree());

        Assert.Contains("Register-ArgumentCompleter -Native -CommandName carbon", script);
    }

    [Fact]
    public void Descriptions_with_quotes_or_colons_cannot_break_the_zsh_string_delimiters()
    {
        var tree = new CompletionNode("carbon", "root", [],
            [new CompletionNode("x", "it's a command: really", [], [])]);

        var script = CompletionCommand.Generate("zsh", tree);

        // The zsh entry is 'name:description' in single quotes; a stray ' or : would corrupt it.
        Assert.DoesNotContain("it's", script);
        Assert.Contains("x", script);
    }
}
