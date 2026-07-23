using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// Everything <c>carbon info</c> needs from outside the process. Behind an interface so the report
/// can be tested against a known toolchain instead of whatever happens to be on the test machine.
/// </summary>
internal interface IEnvironmentProbe
{
    /// <summary>Runs a tool and returns its combined output, or null when it is missing or fails.</summary>
    string? Run(string fileName, params string[] arguments);

    string? GetEnvironmentVariable(string name);

    bool DirectoryExists(string path);

    /// <summary>"macos" | "windows" | "linux".</summary>
    string Platform { get; }

    string Architecture { get; }
}

internal sealed class ProcessProbe : IEnvironmentProbe
{
    /// <summary>A hung probe would hang `carbon info`, and no answer is better than that.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public string Platform =>
        OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux";

    public string Architecture => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? Run(string fileName, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                // Several version probes (notably `java -version`) write to stderr, so both are captured.
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);

            using var process = Process.Start(info);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(Timeout))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }

            output = output.Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            // A missing tool is an ordinary outcome here — the report says "not found".
            return null;
        }
    }
}
