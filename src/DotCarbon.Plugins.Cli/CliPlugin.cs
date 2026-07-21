using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Cli;

/// <summary>
/// Declarative command-line arguments for the packaged app (Task 6.4). The app declares its args and
/// subcommands in <c>plugins.cli</c>; at startup they're parsed from the process arguments and the
/// frontend reads the result with <c>getMatches()</c> — mirroring Tauri's CLI plugin.
/// </summary>
[CarbonPlugin("CLI", description: "Declarative CLI arguments parsed at startup and exposed to the frontend.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("cli:default", "Allow all cli commands.", Commands = new[] { "cli:*" })]
public partial class CliPlugin : IPlugin
{
    private ArgMatches _matches = new([], null);

    public string Namespace => "cli";

    public ValueTask InitializeAsync(PluginContext context)
    {
        var options = context.HasConfiguration
            ? context.GetConfiguration(CliJsonContext.Default.CliOptions)
            : new CliOptions();

        // Skip the executable path; parse the rest against the declared schema.
        var tokens = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _matches = Parse(options.Args ?? [], options.Subcommands ?? [], tokens);
        return ValueTask.CompletedTask;
    }

    /// <summary>The parsed command line for this run.</summary>
    [CarbonCommand("matches")]
    public ArgMatches Matches() => _matches;

    /// <summary>Parses <paramref name="tokens"/> (process args without the executable) against the schema.</summary>
    internal static ArgMatches Parse(
        IReadOnlyList<CliArg> argDefs, IReadOnlyList<CliSubcommand> subcommands, IReadOnlyList<string> tokens)
    {
        var occurrences = argDefs.ToDictionary(def => def.Name, _ => 0, StringComparer.Ordinal);
        var singleValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var multiValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        SubcommandMatch? subcommand = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // The first token that names a subcommand consumes everything after it.
            var subDef = subcommands.FirstOrDefault(sub => sub.Name == token);
            if (subDef is not null)
            {
                var rest = tokens.Skip(i + 1).ToArray();
                subcommand = new SubcommandMatch(subDef.Name, Parse(subDef.Args ?? [], [], rest));
                break;
            }

            var def = ResolveArg(argDefs, token, out var inlineValue);
            if (def is null) continue; // ignore unknown tokens

            occurrences[def.Name]++;
            if (!def.TakesValue) continue;

            var value = inlineValue ?? (i + 1 < tokens.Count ? tokens[++i] : string.Empty);
            if (def.Multiple)
            {
                if (!multiValues.TryGetValue(def.Name, out var list))
                    multiValues[def.Name] = list = [];
                list.Add(value);
            }
            else
            {
                singleValues[def.Name] = value;
            }
        }

        var matches = new Dictionary<string, ArgMatch>(StringComparer.Ordinal);
        foreach (var def in argDefs)
        {
            JsonNode? value;
            if (!def.TakesValue)
                value = JsonValue.Create(occurrences[def.Name] > 0); // flag → bool
            else if (def.Multiple)
                value = multiValues.TryGetValue(def.Name, out var list)
                    ? new JsonArray([.. list.Select(item => (JsonNode?)JsonValue.Create(item))])
                    : null;
            else
                value = singleValues.TryGetValue(def.Name, out var single) ? JsonValue.Create(single) : null;

            matches[def.Name] = new ArgMatch(value, occurrences[def.Name]);
        }

        return new ArgMatches(matches, subcommand);
    }

    private static CliArg? ResolveArg(IReadOnlyList<CliArg> defs, string token, out string? inlineValue)
    {
        inlineValue = null;

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var body = token[2..];
            var eq = body.IndexOf('=', StringComparison.Ordinal);
            var name = eq >= 0 ? body[..eq] : body;
            if (eq >= 0) inlineValue = body[(eq + 1)..];
            return defs.FirstOrDefault(def => def.Long == name || def.Name == name);
        }

        if (token.StartsWith('-') && token.Length > 1)
        {
            var name = token[1..];
            return defs.FirstOrDefault(def => def.Short == name);
        }

        return null;
    }
}
