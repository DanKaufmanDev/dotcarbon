using System.Text.Json.Nodes;

namespace DotCarbon.Plugins.Cli;

/// <summary>A declared CLI argument. A flag (takesValue=false) matches to a boolean; a value argument
/// captures the following token, or an array of them when <c>Multiple</c> is set.</summary>
public record CliArg(
    string Name,
    string? Short = null,
    string? Long = null,
    bool TakesValue = false,
    bool Multiple = false,
    string? Description = null);

/// <summary>A declared subcommand with its own arguments.</summary>
public record CliSubcommand(string Name, CliArg[]? Args = null, string? Description = null);

/// <summary>Plugin configuration (<c>plugins.cli</c>): the app's declared args and subcommands.</summary>
public record CliOptions(CliArg[]? Args = null, CliSubcommand[]? Subcommands = null);

/// <summary>A matched argument: its value (bool for a flag, string or string[] for a value arg) and how
/// many times it appeared.</summary>
public record ArgMatch(JsonNode? Value, int Occurrences);

/// <summary>A matched subcommand and the arguments parsed under it.</summary>
public record SubcommandMatch(string Name, ArgMatches Matches);

/// <summary>The parsed command line, mirroring Tauri's getMatches() shape.</summary>
public record ArgMatches(Dictionary<string, ArgMatch> Args, SubcommandMatch? Subcommand);
