using System.CommandLine;
using System.Diagnostics;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class BuildCommand
{
    public static Command Build()
    {
        var command = new Command("build", "Build Carbon app for production");

        var projectOption = new Option<DirectoryInfo?>(
            "--project",
            "Path to the Carbon project (default: current directory)"
        );

        var targetOption = new Option<string>(
            "--target",
            getDefaultValue: () => GetDefaultTarget(),
            description: "Runtime target (e.g. osx-arm64, win-x64, linux-x64)"
        );

        var noAotOption = new Option<bool>(
            "--no-aot",
            "Skip NativeAOT; emit a single-file self-contained build instead (larger, but no native toolchain required)"
        );

        command.AddOption(projectOption);
        command.AddOption(targetOption);
        command.AddOption(noAotOption);
        command.SetHandler(Run, projectOption, targetOption, noAotOption);

        return command;
    }

    private static async Task Run(DirectoryInfo? projectDir, string target, bool noAot)
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
        Console.WriteLine($"⚡ Carbon build starting (target: {target}, {(noAot ? "single-file" : "NativeAOT")})...");
        Console.ResetColor();

        Console.WriteLine("\n[Carbon] Step 1/2 — Building frontend...");
        var buildSuccess = await BuildFrontend(config, workingDir);
        if (!buildSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Carbon] Frontend build failed. Aborting.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("\n[Carbon] Step 2/2 — Publishing .NET host...");
        await PublishHost(workingDir, target, aot: !noAot);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Build complete → out/{target}/");
        Console.ResetColor();
    }

    private static async Task<bool> BuildFrontend(CarbonConfig config, string workingDir)
    {
        var buildCommand = config.Build.DevCommand
            .Replace("run dev", "run build")
            .Replace(" dev", " build");

        var parts = buildCommand.Split(' ', 2);
        var cmd = parts[0];
        var args = parts.Length > 1 ? parts[1] : "build";

        var distDir = Path.GetDirectoryName(
            Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist))
        ) ?? workingDir;

        var uiDir = FindPackageJson(distDir) ?? workingDir;

        var exitCode = await RunProcessToCompletion(cmd, args, uiDir, "[UI]", ConsoleColor.Green);
        return exitCode == 0;
    }

    private static async Task PublishHost(string workingDir, string target, bool aot)
    {
        var hostProject = Directory
            .GetFiles(workingDir, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(p =>
                File.ReadAllText(p).Contains("DotCarbon.Core") ||
                File.ReadAllText(p).Contains("DotCarbon.Host")
            );

        if (hostProject is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Carbon] Could not find host project.");
            Console.ResetColor();
            return;
        }

        var outputDir = Path.Combine(workingDir, "out", target);

        var publishFlags = aot
            ? "-p:PublishAot=true"
            : "-p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none --self-contained true";

        var args = $"publish \"{hostProject}\" " +
                   $"--runtime {target} " +
                   $"--configuration Release " +
                   $"--output \"{outputDir}\" " +
                   publishFlags;

        await RunProcessToCompletion("dotnet", args, workingDir, "[C#]", ConsoleColor.Magenta);
    }

    private static async Task<int> RunProcessToCompletion(
        string command, string args, string workingDir,
        string prefix, ConsoleColor color)
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

        using var process = new Process { StartInfo = psi };

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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return process.ExitCode;
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

    private static string GetDefaultTarget()
    {
        if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64
                ? "osx-arm64"
                : "osx-x64";
        }
        if (OperatingSystem.IsWindows()) return "win-x64";
        return "linux-x64";
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