using System.CommandLine;
using System.Text;

namespace DotCarbon.Cli.Commands;

/// <summary>A command in the tree, reduced to what a completion script needs.</summary>
internal sealed record CompletionNode(
    string Name,
    string Description,
    IReadOnlyList<string> Options,
    IReadOnlyList<CompletionNode> Subcommands);

/// <summary>
/// <c>carbon completions bash|zsh|fish|pwsh</c> — prints a shell completion script for the whole
/// command tree. The scripts are generated from the live tree (via <see cref="CompletionNode"/>), so
/// they never drift from the real commands and options the way a hand-written script would.
/// </summary>
public static class CompletionCommand
{
    private static readonly string[] Shells = ["bash", "zsh", "fish", "pwsh"];

    public static Command Build(RootCommand root)
    {
        var command = new Command("completions", "Print a shell completion script (bash, zsh, fish, pwsh)");
        var shell = new Argument<string>("shell", $"One of: {string.Join(", ", Shells)}");
        command.AddArgument(shell);

        command.SetHandler(context =>
        {
            var requested = context.ParseResult.GetValueForArgument(shell).Trim().ToLowerInvariant();
            if (!Shells.Contains(requested))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[Carbon] Unknown shell '{requested}'. Use one of: {string.Join(", ", Shells)}");
                Console.ResetColor();
                context.ExitCode = 1;
                return;
            }

            // Model the whole tree — root is fully populated by the time a handler runs.
            Console.Out.Write(Generate(requested, Model(root)));
        });

        return command;
    }

    /// <summary>Reduce a live command to the completion model (recursively).</summary>
    internal static CompletionNode Model(Command command) => new(
        command is RootCommand ? "carbon" : command.Name,
        Sanitize(command.Description),
        OptionFlags(command),
        command.Subcommands.Select(Model).ToList());

    private static IReadOnlyList<string> OptionFlags(Command command)
    {
        var flags = command.Options
            .SelectMany(option => option.Aliases.Count > 0 ? option.Aliases : [$"--{option.Name}"])
            .Where(alias => alias.StartsWith('-'))
            .ToList();
        // Every command answers --help; the parser adds it but it is not in Options.
        if (!flags.Contains("--help")) flags.Add("--help");
        return flags.Distinct().OrderBy(flag => flag, StringComparer.Ordinal).ToList();
    }

    internal static string Generate(string shell, CompletionNode root) => shell switch
    {
        "bash" => Bash(root),
        "zsh" => Zsh(root),
        "fish" => Fish(root),
        "pwsh" => Pwsh(root),
        _ => throw new ArgumentOutOfRangeException(nameof(shell)),
    };

    // ---- bash ---------------------------------------------------------------------------------

    private static string Bash(CompletionNode root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# carbon bash completion. Install: carbon completions bash > /etc/bash_completion.d/carbon");
        // Deliberately does not use `_init_completion` — that needs the bash-completion package, which
        // is absent on stock bash (e.g. macOS). COMP_WORDS/COMP_CWORD are built in.
        builder.AppendLine("_carbon() {");
        builder.AppendLine("    local cur cmd i");
        builder.AppendLine("    COMPREPLY=()");
        builder.AppendLine("    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        builder.AppendLine();
        // A space-separated path of the subcommands seen so far selects the candidate set.
        builder.AppendLine("    cmd=\"\"");
        builder.AppendLine("    for (( i=1; i < COMP_CWORD; i++ )); do");
        builder.AppendLine("        case \"${COMP_WORDS[i]}\" in");
        builder.AppendLine("            -*) ;;");
        builder.AppendLine("            *) cmd=\"${cmd:+$cmd }${COMP_WORDS[i]}\" ;;");
        builder.AppendLine("        esac");
        builder.AppendLine("    done");
        builder.AppendLine();
        builder.AppendLine("    local candidates=\"\"");
        builder.AppendLine("    case \"$cmd\" in");
        foreach (var (path, node) in Flatten(root))
        {
            var words = string.Join(" ", node.Subcommands.Select(child => child.Name).Concat(node.Options));
            builder.AppendLine($"        \"{SubPath(path)}\") candidates=\"{words}\" ;;");
        }
        builder.AppendLine("    esac");
        builder.AppendLine();
        builder.AppendLine("    COMPREPLY=( $(compgen -W \"$candidates\" -- \"$cur\") )");
        builder.AppendLine("}");
        builder.AppendLine("complete -F _carbon carbon");
        return builder.ToString();
    }

    // ---- zsh ----------------------------------------------------------------------------------

    private static string Zsh(CompletionNode root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#compdef carbon");
        builder.AppendLine("# carbon zsh completion. Install: carbon completions zsh > \"${fpath[1]}/_carbon\"");
        builder.AppendLine("_carbon() {");
        builder.AppendLine("    local -a words_path");
        builder.AppendLine("    local w");
        builder.AppendLine("    for w in ${words[2,$(( CURRENT - 1 ))]}; do");
        builder.AppendLine("        [[ $w == -* ]] || words_path+=$w");
        builder.AppendLine("    done");
        builder.AppendLine("    local cmd=\"${(j: :)words_path}\"");
        builder.AppendLine();
        builder.AppendLine("    local -a candidates");
        builder.AppendLine("    case \"$cmd\" in");
        foreach (var (path, node) in Flatten(root))
        {
            builder.AppendLine($"        \"{SubPath(path)}\")");
            builder.AppendLine("            candidates=(");
            foreach (var child in node.Subcommands)
                builder.AppendLine($"                '{child.Name}:{Escape(child.Description)}'");
            foreach (var option in node.Options)
                builder.AppendLine($"                '{option}'");
            builder.AppendLine("            ) ;;");
        }
        builder.AppendLine("    esac");
        builder.AppendLine("    _describe 'carbon' candidates");
        builder.AppendLine("}");
        builder.AppendLine("_carbon \"$@\"");
        return builder.ToString();
    }

    // ---- fish ---------------------------------------------------------------------------------

    private static string Fish(CompletionNode root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# carbon fish completion. Install: carbon completions fish > ~/.config/fish/completions/carbon.fish");
        // Top-level subcommands, offered only when no subcommand has been given yet.
        foreach (var child in root.Subcommands)
            builder.AppendLine(
                $"complete -c carbon -f -n __fish_use_subcommand -a {child.Name} -d '{Escape(child.Description)}'");

        foreach (var (path, node) in Flatten(root))
        {
            if (path == "carbon") continue;
            // "carbon bundle android" -> condition on the last word being "android".
            var last = path.Split(' ')[^1];
            foreach (var child in node.Subcommands)
                builder.AppendLine(
                    $"complete -c carbon -f -n '__fish_seen_subcommand_from {last}' -a {child.Name} " +
                    $"-d '{Escape(child.Description)}'");
            foreach (var option in node.Options.Where(o => o.StartsWith("--")))
                builder.AppendLine(
                    $"complete -c carbon -f -n '__fish_seen_subcommand_from {last}' -l {option[2..]}");
        }

        return builder.ToString();
    }

    // ---- pwsh ---------------------------------------------------------------------------------

    private static string Pwsh(CompletionNode root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# carbon PowerShell completion. Install: carbon completions pwsh | Out-String | Invoke-Expression");
        builder.AppendLine("Register-ArgumentCompleter -Native -CommandName carbon -ScriptBlock {");
        builder.AppendLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        builder.AppendLine("    $elements = $commandAst.CommandElements | Select-Object -Skip 1 |");
        builder.AppendLine("        ForEach-Object { $_.ToString() } | Where-Object { $_ -notlike '-*' -and $_ -ne $wordToComplete }");
        builder.AppendLine("    $cmd = ($elements -join ' ')");
        builder.AppendLine("    $candidates = switch ($cmd) {");
        foreach (var (path, node) in Flatten(root))
        {
            var words = node.Subcommands.Select(child => child.Name).Concat(node.Options);
            builder.AppendLine($"        '{SubPath(path)}' {{ @({string.Join(", ", words.Select(w => $"'{w}'"))}) }}");
        }
        builder.AppendLine("        default { @() }");
        builder.AppendLine("    }");
        builder.AppendLine("    $candidates | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object {");
        builder.AppendLine("        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    // ---- shared -------------------------------------------------------------------------------

    /// <summary>Every command in the tree, keyed by its full space-separated path ("carbon bundle android").</summary>
    internal static IReadOnlyList<(string Path, CompletionNode Node)> Flatten(CompletionNode root)
    {
        var result = new List<(string, CompletionNode)>();
        void Walk(string prefix, CompletionNode node)
        {
            var path = prefix.Length == 0 ? node.Name : $"{prefix} {node.Name}";
            result.Add((path, node));
            foreach (var child in node.Subcommands) Walk(path, child);
        }

        Walk(string.Empty, root);
        return result;
    }

    /// <summary>
    /// The command path with the leading <c>carbon</c> removed — <c>""</c> for the root, <c>"bundle"</c>,
    /// <c>"bundle android"</c>. The shell functions accumulate the words *after* <c>carbon</c>, so the
    /// script's case labels must match that, not the full path.
    /// </summary>
    internal static string SubPath(string path) =>
        path == "carbon" ? string.Empty : path["carbon ".Length..];

    private static string Sanitize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? string.Empty : description.Replace('\n', ' ').Trim();

    /// <summary>Single quotes are the string delimiter in the zsh/fish scripts, so they must go.</summary>
    private static string Escape(string text) => text.Replace("'", " ").Replace(':', ' ');
}
