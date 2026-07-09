using System.CommandLine;
using System.Diagnostics;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class DevCommand
{
    public static Command Build()
    {
        var command = new Command("dev", "Start Carbon in development mode");
        var projectOption = new Option<DirectoryInfo?>(
            name: "--project",
            description: "Path to the Carbon project (default: current directory)"
        );

        command.AddOption(projectOption);
        command.SetHandler(Run, projectOption);

        return command;
    }

    private static async Task Run(DirectoryInfo? projectDir)
    {
        var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");

        if (!File.Exists(configPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Carbon] No carbon.json found in {workingDir}");
            Console.ResetColor();
            return;
        }

        var config = ConfigLoader.Load(configPath);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚡ Carbon dev mode starting...");
        Console.ResetColor();

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Carbon] Shutting down...");
            cts.Cancel();
        };
        await Task.WhenAny(
            RunFrontend(config, workingDir, cts.Token),
            RunHost(workingDir, cts.Token)
        );

        cts.Cancel();
        Console.WriteLine("[Carbon] Done.");
    }

    private static async Task RunFrontend(CarbonConfig config, string workingDir, CancellationToken ct)
    {
        var parts = config.Build.DevCommand.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : string.Empty;
        var frontendDir = Path.GetFullPath(
            Path.Combine(workingDir, Path.GetDirectoryName(config.Build.FrontendDist) ?? "ui")
        );

        var packageJsonDir = FindPackageJson(frontendDir) ?? workingDir;

        Console.WriteLine($"[Carbon] Starting frontend: {config.Build.DevCommand}");
        Console.WriteLine($"[Carbon] Frontend dir: {packageJsonDir}");

        await RunProcess(command, args, packageJsonDir, "[UI]", ConsoleColor.Green, ct);
    }

    private static async Task RunHost(string workingDir, CancellationToken ct)
    {
        await Task.Delay(2000, ct);

        Console.WriteLine("[Carbon] Starting .NET host...");

        var hostProject = FindHostProject(workingDir);

        if (hostProject is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Carbon] Could not find host .csproj. Is there a project referencing DotCarbon.Core?");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"[Carbon] Host project: {hostProject}");

        await RunProcess(
            "dotnet", $"run --project \"{hostProject}\"",
            workingDir, "[C#]", ConsoleColor.Magenta, ct
        );
    }

    private static async Task RunProcess(
        string command,
        string args,
        string workingDir,
        string prefix,
        ConsoleColor color,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.ForegroundColor = color;
            Console.Write($"{prefix} ");
            Console.ResetColor();
            Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{prefix} ");
            Console.ResetColor();
            Console.WriteLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{prefix} Failed to start: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static string? FindPackageJson(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

private static string? FindHostProject(string workingDir)
{
    return Directory
        .GetFiles(workingDir, "*.csproj", SearchOption.AllDirectories)
        .FirstOrDefault(proj => {
            var content = File.ReadAllText(proj);
            return content.Contains("Photino") &&
                   content.Contains("<OutputType>Exe</OutputType>");
        });
}
}