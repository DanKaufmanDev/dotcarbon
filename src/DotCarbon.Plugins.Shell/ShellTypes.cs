namespace DotCarbon.Plugins.Shell;

public record ExecuteArgs(
    string Program,
    string[]? Args = null,
    string? Cwd = null,
    Dictionary<string, string>? Env = null
);

public record ShellOptions(
    string[]? AllowedPrograms = null,
    string[]? AllowedCwds = null,
    string[]? AllowedUrlSchemes = null,
    bool AllowOpenPaths = false
);

public record ShellResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool Success
);

public record OpenArgs(
    string Path
);
