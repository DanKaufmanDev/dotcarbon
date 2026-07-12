namespace DotCarbon.Cli.Bundling;

internal sealed record PublishOutputValidation(
    bool Success,
    string OutputDirectory,
    string? ExecutablePath = null,
    string? Error = null,
    IReadOnlyList<string>? Files = null)
{
    public IReadOnlyList<string> Files { get; init; } = Files ?? [];
}

internal static class PublishOutputVerifier
{
    public static PublishOutputValidation Verify(string outputDir, string target, bool allowSidecars)
    {
        if (!Directory.Exists(outputDir))
            return Fail(outputDir, $"Publish output directory does not exist: {outputDir}");

        var files = Directory.GetFiles(outputDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            return Fail(outputDir, $"Publish output directory is empty: {outputDir}");

        var executable = FindExecutable(files, target);
        if (executable is null)
            return Fail(outputDir, "Publish output does not contain a launcher executable.");

        if (new FileInfo(executable).Length == 0)
            return Fail(outputDir, $"Launcher executable is empty: {Path.GetFileName(executable)}");

        if (!allowSidecars && files.Length != 1)
        {
            var names = string.Join(", ", files.Select(Path.GetFileName));
            return Fail(outputDir,
                "Single-file publish produced extra files: " + names +
                ". Use --aot for the experimental sidecar mode, or check the host project's publish settings.",
                executable,
                files);
        }

        if (allowSidecars && files.Count(file => IsExecutable(file, target)) != 1)
            return Fail(outputDir, "AOT sidecar mode must contain exactly one launcher executable.", executable, files);

        return new PublishOutputValidation(true, outputDir, executable, Files: files);
    }

    private static PublishOutputValidation Fail(
        string outputDir,
        string error,
        string? executable = null,
        IReadOnlyList<string>? files = null) =>
        new(false, outputDir, executable, error, files);

    private static string? FindExecutable(IEnumerable<string> files, string target) =>
        files.FirstOrDefault(file => IsExecutable(file, target));

    private static bool IsExecutable(string path, string target)
    {
        if (target.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

        if (Path.GetExtension(path).Length > 0)
            return false;

        if (OperatingSystem.IsWindows())
            return false;

        try
        {
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & execute) != 0;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
