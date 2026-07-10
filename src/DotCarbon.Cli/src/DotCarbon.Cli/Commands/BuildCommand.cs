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

        BundleFrontend(config, workingDir, target);

        var artifact = $"out/{target}/";
        if (target.StartsWith("osx"))
            artifact = await BundleMac(config, workingDir, target) ?? artifact;
        else if (target.StartsWith("win"))
            Console.WriteLine("[Carbon] Windows .exe is in the output. Build a .msi with WiX/velopack if you need an installer.");
        else if (target.StartsWith("linux"))
            Console.WriteLine("[Carbon] Linux binary is in the output. Package an .AppImage with appimagetool if you need one.");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ Build complete → {artifact}");
        Console.ResetColor();
    }

    private static void BundleFrontend(CarbonConfig config, string workingDir, string target)
    {
        var src = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        if (!Directory.Exists(src))
        {
            Console.WriteLine("[Carbon] Warning: frontend dist not found — the app would show the fallback screen.");
            return;
        }
        CopyDir(src, Path.Combine(workingDir, "out", target, config.Build.FrontendDist));
        Console.WriteLine($"[Carbon] Bundled web UI → out/{target}/{config.Build.FrontendDist}");
    }

    private static async Task<string?> BundleMac(CarbonConfig config, string workingDir, string target)
    {
        if (!OperatingSystem.IsMacOS()) return null;

        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir)
            .FirstOrDefault(f => Path.GetExtension(f).Length == 0 && !f.EndsWith(".dylib"));
        if (exe is null) return null;

        var exeName = Path.GetFileName(exe);
        var appName = string.IsNullOrWhiteSpace(config.App.Name) ? exeName : config.App.Name;
        Console.WriteLine("\n[Carbon] Packaging macOS .app + .dmg...");

        var app = Path.Combine(outDir, appName + ".app");
        var dmg = Path.Combine(outDir, appName + ".dmg");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        if (File.Exists(dmg)) File.Delete(dmg);

        var macos = Path.Combine(app, "Contents", "MacOS");
        Directory.CreateDirectory(macos);
        Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources"));

        foreach (var f in Directory.GetFiles(outDir))
            File.Copy(f, Path.Combine(macos, Path.GetFileName(f)), true);
        var dist = Path.Combine(outDir, config.Build.FrontendDist);
        if (Directory.Exists(dist))
            CopyDir(dist, Path.Combine(macos, config.Build.FrontendDist));

        await File.WriteAllTextAsync(Path.Combine(app, "Contents", "Info.plist"), InfoPlist(config, exeName, appName));
        await RunProcessToCompletion("chmod", $"+x \"{Path.Combine(macos, exeName)}\"", outDir, "[pkg]", ConsoleColor.Blue);
        await RunProcessToCompletion("hdiutil",
            $"create -volname \"{appName}\" -srcfolder \"{app}\" -ov -format UDZO \"{dmg}\"",
            outDir, "[pkg]", ConsoleColor.Blue);

        return File.Exists(dmg) ? $"out/{target}/{appName}.dmg" : null;
    }

    private static string InfoPlist(CarbonConfig config, string exeName, string appName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\">\n<dict>\n" +
        $"    <key>CFBundleName</key><string>{appName}</string>\n" +
        $"    <key>CFBundleDisplayName</key><string>{config.App.Name}</string>\n" +
        $"    <key>CFBundleIdentifier</key><string>{config.App.Identifier}</string>\n" +
        $"    <key>CFBundleVersion</key><string>{config.App.Version}</string>\n" +
        $"    <key>CFBundleShortVersionString</key><string>{config.App.Version}</string>\n" +
        $"    <key>CFBundleExecutable</key><string>{exeName}</string>\n" +
        "    <key>CFBundlePackageType</key><string>APPL</string>\n" +
        "    <key>NSHighResolutionCapable</key><true/>\n" +
        "</dict>\n</plist>\n";

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(src, dst));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), true);
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