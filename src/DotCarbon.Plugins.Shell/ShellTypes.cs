namespace DotCarbon.Plugins.Shell;

public record ExecuteArgs(
    string Program,
    string[]? Args = null,
    string? Cwd = null,
    Dictionary<string, string>? Env = null,
    // When true, Program names a bundled sidecar (e.g. "binaries/my-tool") resolved next to the app,
    // rather than a program that must appear in allowedPrograms.
    bool Sidecar = false
);

public record ShellOptions(
    string[]? AllowedPrograms = null,
    string[]? AllowedCwds = null,
    string[]? AllowedUrlSchemes = null,
    bool AllowOpenPaths = false,
    string[]? AllowedEnv = null,
    string[]? DeniedEnv = null
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
