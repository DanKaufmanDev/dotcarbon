using System.Diagnostics;
using System.Xml.Linq;
using DotCarbon.Cli.Commands;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Packages a Carbon app for Android by publishing the generated .NET Android project
/// (<c>.carbon/platforms/android</c>) to an APK or AAB. Requires the Android workload.
/// </summary>
internal sealed class AndroidBundler
{
    public BundlePlan Plan(CarbonConfig config, string format, bool release)
    {
        var configuration = release ? "Release" : "Debug";
        return new BundlePlan
        {
            TargetId = "android",
            TargetName = $"Android ({configuration}, .{format})",
            Summary = $"one Carbon app → .{format} via .NET Android",
            Steps = new List<BundleStep>
            {
                new("Validate", "android platform added (`carbon platform add android`) + Android workload"),
                new("Build frontend", "vite build → embedded into the Android app assembly"),
                new("Publish .NET Android", $"dotnet publish -f net10.0-android -c {configuration} (AndroidPackageFormat={format})"),
                new("Locate artifact", $"the .{format} under the project's bin/{configuration} output"),
            },
        };
    }

    public async Task<int> ExecuteAsync(
        CarbonConfig config, string workingDir, string format, bool release, bool dryRun)
    {
        var androidDir = PlatformService.PlatformDir(workingDir, "android");
        var project = Directory.Exists(androidDir)
            ? Directory.GetFiles(androidDir, "*.csproj").FirstOrDefault()
            : null;

        if (project is null)
        {
            Error("No Android project found. Run `carbon platform add android` first.");
            return 1;
        }

        if (dryRun)
        {
            Plan(config, format, release).Render(dryRun: true);
            return 0;
        }

        Plan(config, format, release).Render(dryRun: false);

        if (!await HasAndroidWorkload())
        {
            Error("The .NET Android workload is not installed. Run: dotnet workload install android");
            return 1;
        }

        Console.WriteLine("\n[Carbon] Step 1/2 — Building frontend...");
        if (!await BuildCommand.BuildFrontend(config, workingDir))
        {
            Error("Frontend build failed. Aborting.");
            return 1;
        }

        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        if (!File.Exists(Path.Combine(frontendDist, "index.html")))
        {
            Error($"Frontend output does not contain index.html: {frontendDist}");
            return 1;
        }
        var configPath = Path.Combine(workingDir, "carbon.json");
        var props = WriteEmbedProps(androidDir, project, frontendDist, configPath);

        Console.WriteLine("\n[Carbon] Step 2/2 — Publishing .NET Android app...");
        var configuration = release ? "Release" : "Debug";
        var args =
            $"publish \"{project}\" -c {configuration} -f net10.0-android " +
            $"-p:AndroidPackageFormat={format} " +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        if (await BuildCommand.RunProcessToCompletion("dotnet", args, androidDir, "[android]", ConsoleColor.Magenta) != 0)
        {
            Error(".NET Android publish failed.");
            return 1;
        }

        var artifact = Directory
            .EnumerateFiles(androidDir, $"*.{format}", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(artifact is not null
            ? $"\n[Carbon] Build complete -> {Path.GetRelativePath(workingDir, artifact)}"
            : $"\n[Carbon] Publish finished; look for the .{format} under {Path.GetRelativePath(workingDir, androidDir)}/bin/{configuration}.");
        Console.ResetColor();
        return 0;
    }

    /// <summary>
    /// Debug build + deploy/run on a connected device or emulator (`dotnet build -t:Run`).
    /// A hot-reload dev loop (Vite over the emulator's 10.0.2.2 host bridge) is roadmap Phase 11.
    /// </summary>
    public async Task<int> DevAsync(CarbonConfig config, string workingDir)
    {
        var androidDir = PlatformService.PlatformDir(workingDir, "android");
        var project = Directory.Exists(androidDir)
            ? Directory.GetFiles(androidDir, "*.csproj").FirstOrDefault()
            : null;
        if (project is null)
        {
            Error("No Android project found. Run `carbon platform add android` first.");
            return 1;
        }
        if (!await HasAndroidWorkload())
        {
            Error("The .NET Android workload is not installed. Run: dotnet workload install android");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚡ Carbon android dev — building and deploying to a device/emulator...");
        Console.ResetColor();
        Console.WriteLine("  (needs a running emulator or a connected device via adb)");

        if (!await BuildCommand.BuildFrontend(config, workingDir))
        {
            Error("Frontend build failed. Aborting.");
            return 1;
        }
        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        var props = WriteEmbedProps(androidDir, project, frontendDist, Path.Combine(workingDir, "carbon.json"));

        var args =
            $"build \"{project}\" -c Debug -f net10.0-android -t:Run " +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        return await BuildCommand.RunProcessToCompletion("dotnet", args, androidDir, "[android]", ConsoleColor.Magenta);
    }

    private static string WriteEmbedProps(string androidDir, string project, string frontendDist, string configPath)
    {
        var generatedDir = Path.Combine(androidDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var propsPath = Path.Combine(generatedDir, "DotCarbon.Android.props");
        var condition = $"'$(MSBuildProjectFullPath)' == '{project.Replace("'", "%27")}'";
        var document = new XDocument(
            new XElement("Project",
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

    private static async Task<bool> HasAndroidWorkload()
    {
        try
        {
            var info = new ProcessStartInfo("dotnet", "workload list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(info);
            if (process is null) return false;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Contains("android", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
