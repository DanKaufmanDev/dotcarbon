using System.CommandLine;
using System.Diagnostics;
using System.Xml.Linq;
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

        var aotOption = new Option<bool>(
            "--aot",
            "Use NativeAOT (experimental; Photino's native library remains a second file)"
        );
        var bundleOption = new Option<bool>(
            "--bundle",
            "Also create the platform installer/package (.dmg, .msi, or .AppImage)"
        );

        command.AddOption(projectOption);
        command.AddOption(targetOption);
        command.AddOption(aotOption);
        command.AddOption(bundleOption);
        command.SetHandler(async context =>
        {
            context.ExitCode = await Run(
                context.ParseResult.GetValueForOption(projectOption),
                context.ParseResult.GetValueForOption(targetOption)!,
                context.ParseResult.GetValueForOption(aotOption),
                context.ParseResult.GetValueForOption(bundleOption));
        });

        return command;
    }

    private static async Task<int> Run(DirectoryInfo? projectDir, string target, bool aot, bool bundle)
    {
        var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");

        if (!File.Exists(configPath))
        {
            WriteError($"No carbon.json found in {workingDir}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Carbon] Build starting (target: {target}, {(aot ? "NativeAOT" : "single-file")})...");
        Console.ResetColor();

        Console.WriteLine("\n[Carbon] Step 1/2 — Building frontend...");
        var buildSuccess = await BuildFrontend(config, workingDir);
        if (!buildSuccess)
        {
            WriteError("Frontend build failed. Aborting.");
            return 1;
        }

        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        if (!File.Exists(Path.Combine(frontendDist, "index.html")))
        {
            WriteError($"Frontend output does not contain index.html: {frontendDist}");
            return 1;
        }

        var hostProject = ProjectLocator.FindHostProject(workingDir, config);
        if (hostProject is null)
        {
            WriteError("Could not identify the executable host project. Set build.backendProject in carbon.json.");
            return 1;
        }

        Console.WriteLine($"[Carbon] Host project: {Path.GetRelativePath(workingDir, hostProject)}");
        Console.WriteLine("\n[Carbon] Step 2/2 - Embedding frontend and publishing .NET host...");
        var bundleProps = WriteBundleProps(workingDir, hostProject, frontendDist, configPath, aot);
        if (!await PublishHost(hostProject, workingDir, target, bundleProps))
        {
            WriteError(".NET publish failed.");
            return 1;
        }

        var outputDir = Path.Combine(workingDir, "out", target);
        RemovePublishSymbols(outputDir);
        var executable = FindExecutable(outputDir, target);
        if (executable is null)
        {
            WriteError("Publish completed but no executable was produced.");
            return 1;
        }

        var artifact = Path.GetRelativePath(workingDir, executable);
        if (bundle && target.StartsWith("osx"))
            artifact = await BundleMac(config, workingDir, target) ?? artifact;
        else if (bundle && target.StartsWith("win"))
            artifact = await BundleWindows(config, workingDir, target) ?? artifact;
        else if (bundle && target.StartsWith("linux"))
            artifact = await BundleLinux(config, workingDir, target) ?? artifact;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Carbon] Build complete -> {artifact}");
        Console.ResetColor();
        return 0;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }

    private static async Task<string?> BundleMac(CarbonConfig config, string workingDir, string target)
    {
        if (!OperatingSystem.IsMacOS()) return null;

        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir).FirstOrDefault(IsUnixExecutable);
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

    private static async Task<string?> BundleWindows(CarbonConfig config, string workingDir, string target)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir, "*.exe").FirstOrDefault();
        if (exe is null) return null;
        if (!ToolExists("wix"))
        {
            Console.WriteLine("[Carbon] The .exe is in the output. For a .msi: dotnet tool install --global wix --version 4.* then rebuild.");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Windows .msi (WiX)...");
        var name = string.IsNullOrWhiteSpace(config.App.Name) ? Path.GetFileNameWithoutExtension(exe) : config.App.Name;
        var wxs = Path.Combine(workingDir, "out", "installer.wxs");
        var msi = Path.Combine(workingDir, "out", name + ".msi");
        await File.WriteAllTextAsync(wxs,
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n" +
            $"  <Package Name=\"{name}\" Manufacturer=\"{config.App.Name}\" Version=\"{MsiVersion(config.App.Version)}\" UpgradeCode=\"{StableGuid(config.App.Identifier)}\">\n" +
            "    <MajorUpgrade DowngradeErrorMessage=\"A newer version is already installed.\" />\n" +
            "    <MediaTemplate EmbedCab=\"yes\" />\n" +
            "    <StandardDirectory Id=\"ProgramFiles64Folder\">\n" +
            $"      <Directory Id=\"INSTALLFOLDER\" Name=\"{name}\">\n" +
            $"        <Files Include=\"{outDir}\\**\" />\n" +
            "      </Directory>\n" +
            "    </StandardDirectory>\n" +
            "  </Package>\n</Wix>\n");
        await RunProcessToCompletion("wix", $"build \"{wxs}\" -o \"{msi}\"", outDir, "[pkg]", ConsoleColor.Blue);
        return File.Exists(msi) ? $"out/{name}.msi" : null;
    }

    private static async Task<string?> BundleLinux(CarbonConfig config, string workingDir, string target)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir).FirstOrDefault(IsUnixExecutable);
        if (exe is null) return null;
        var tool = await EnsureAppImageTool(workingDir);
        if (tool is null)
        {
            Console.WriteLine("[Carbon] The binary is in the output. Install appimagetool for an .AppImage.");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Linux .AppImage...");
        var exeName = Path.GetFileName(exe);
        var name = string.IsNullOrWhiteSpace(config.App.Name) ? exeName : config.App.Name;
        var slug = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        var appdir = Path.Combine(outDir, "AppDir");
        if (Directory.Exists(appdir)) Directory.Delete(appdir, true);
        var bin = Path.Combine(appdir, "usr", "bin");
        Directory.CreateDirectory(bin);

        foreach (var f in Directory.GetFiles(outDir)) File.Copy(f, Path.Combine(bin, Path.GetFileName(f)), true);

        await File.WriteAllTextAsync(Path.Combine(appdir, "AppRun"),
            "#!/bin/sh\nHERE=\"$(dirname \"$(readlink -f \"$0\")\")\"\ncd \"$HERE/usr/bin\"\nexec \"./" + exeName + "\" \"$@\"\n");
        await File.WriteAllTextAsync(Path.Combine(appdir, slug + ".desktop"),
            "[Desktop Entry]\nType=Application\nName=" + name + "\nExec=" + exeName + "\nIcon=" + slug + "\nCategories=Utility;\n");
        await File.WriteAllBytesAsync(Path.Combine(appdir, slug + ".png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

        await RunProcessToCompletion("chmod", $"+x \"{Path.Combine(appdir, "AppRun")}\" \"{Path.Combine(bin, exeName)}\"", outDir, "[pkg]", ConsoleColor.Blue);
        Environment.SetEnvironmentVariable("ARCH", "x86_64");
        Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", "1");
        var appimage = Path.Combine(outDir, name + ".AppImage");
        await RunProcessToCompletion(tool, $"\"{appdir}\" \"{appimage}\"", outDir, "[pkg]", ConsoleColor.Blue);
        return File.Exists(appimage) ? $"out/{target}/{name}.AppImage" : null;
    }

    private static async Task<string?> EnsureAppImageTool(string workingDir)
    {
        if (ToolExists("appimagetool")) return "appimagetool";
        var local = Path.Combine(workingDir, "out", "appimagetool");
        if (!File.Exists(local))
        {
            Console.WriteLine("[Carbon] Downloading appimagetool...");
            var code = await RunProcessToCompletion("curl",
                $"-fsSL -o \"{local}\" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage",
                workingDir, "[pkg]", ConsoleColor.Blue);
            if (code != 0 || !File.Exists(local)) return null;
            await RunProcessToCompletion("chmod", $"+x \"{local}\"", workingDir, "[pkg]", ConsoleColor.Blue);
        }
        return local;
    }

    private static bool ToolExists(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "--version")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p!.WaitForExit(5000);
            return true;
        }
        catch { return false; }
    }

    private static string MsiVersion(string? v)
    {
        var nums = (v ?? "0.0.0").Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0).ToList();
        while (nums.Count < 4) nums.Add(0);
        return string.Join(".", nums.Take(4));
    }

    private static Guid StableGuid(string s)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? "com.example.app")));
    }

    private static async Task<bool> BuildFrontend(CarbonConfig config, string workingDir)
    {
        var buildCommand = string.IsNullOrWhiteSpace(config.Build.BuildCommand)
            ? config.Build.DevCommand.Replace("run dev", "run build").Replace(" dev", " build")
            : config.Build.BuildCommand;

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

    private static string WriteBundleProps(
        string workingDir,
        string hostProject,
        string frontendDist,
        string configPath,
        bool aot)
    {
        var generatedDir = Path.Combine(workingDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var propsPath = Path.Combine(generatedDir, "DotCarbon.Bundle.props");
        var condition = $"'$(MSBuildProjectFullPath)' == '{hostProject.Replace("'", "%27")}'";
        var document = new XDocument(
            new XElement("Project",
                new XElement("PropertyGroup",
                    new XAttribute("Condition", condition),
                    new XElement("PublishAot", aot.ToString().ToLowerInvariant()),
                    new XElement("PublishSingleFile", (!aot).ToString().ToLowerInvariant()),
                    new XElement("IncludeNativeLibrariesForSelfExtract", (!aot).ToString().ToLowerInvariant()),
                    new XElement("EnableCompressionInSingleFile", (!aot).ToString().ToLowerInvariant()),
                    new XElement("PublishTrimmed", true),
                    new XElement("TrimMode", "partial"),
                    new XElement("DebugType", "None"),
                    new XElement("DebugSymbols", false),
                    new XElement("CopyOutputSymbolsToPublishDirectory", false),
                    new XElement("StripSymbols", aot.ToString().ToLowerInvariant())),
                new XElement("ItemGroup",
                    new XAttribute("Condition", condition),
                    new XElement("EmbeddedResource",
                        new XAttribute("Include", Path.Combine(frontendDist, "**", "*")),
                        new XAttribute("LogicalName", "DotCarbon.Assets/%(RecursiveDir)%(Filename)%(Extension)")),
                    new XElement("EmbeddedResource",
                        new XAttribute("Include", configPath),
                        new XAttribute("LogicalName", "DotCarbon.Config/carbon.json")))));
        document.Save(propsPath);
        return propsPath;
    }

    private static async Task<bool> PublishHost(
        string hostProject,
        string workingDir,
        string target,
        string bundleProps)
    {

        var outputDir = Path.Combine(workingDir, "out", target);
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);

        var args = $"publish \"{hostProject}\" " +
                   $"--runtime {target} " +
                   $"--configuration Release " +
                   $"--output \"{outputDir}\" " +
                   $"--self-contained true " +
                   $"-p:CustomBeforeMicrosoftCommonProps=\"{bundleProps}\"";

        return await RunProcessToCompletion("dotnet", args, workingDir, "[C#]", ConsoleColor.Magenta) == 0;
    }

    private static string? FindExecutable(string outputDir, string target)
    {
        if (!Directory.Exists(outputDir)) return null;
        return target.StartsWith("win", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetFiles(outputDir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : Directory.GetFiles(outputDir, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(IsUnixExecutable);
    }

    private static void RemovePublishSymbols(string outputDir)
    {
        foreach (var pattern in new[] { "*.pdb", "*.dbg" })
            foreach (var file in Directory.EnumerateFiles(outputDir, pattern, SearchOption.TopDirectoryOnly))
                File.Delete(file);

        foreach (var directory in Directory.EnumerateDirectories(outputDir, "*.dSYM", SearchOption.TopDirectoryOnly))
            Directory.Delete(directory, recursive: true);
    }

    private static bool IsUnixExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & execute) != 0;
        }
        catch (PlatformNotSupportedException) { return false; }
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
