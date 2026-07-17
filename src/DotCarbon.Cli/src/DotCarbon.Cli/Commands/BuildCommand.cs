using System.CommandLine;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using DotCarbon.Cli.Bundling;
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
            description: "Runtime target (e.g. osx-arm64, osx-universal, win-x64, linux-x64)"
        );

        var aotOption = new Option<bool>(
            "--aot",
            "Use NativeAOT (experimental; Photino's native library remains a second file)"
        );
        var bundleOption = new Option<bool>(
            "--bundle",
            "Also create the platform installer/package (.dmg, .msi, or .AppImage)"
        );
        var updaterArtifactsOption = new Option<bool>(
            "--updater-artifacts",
            "Create and sign updater metadata (requires CARBON_UPDATER_PRIVATE_KEY)"
        );

        command.AddOption(projectOption);
        command.AddOption(targetOption);
        command.AddOption(aotOption);
        command.AddOption(bundleOption);
        command.AddOption(updaterArtifactsOption);
        command.SetHandler(async context =>
        {
            context.ExitCode = await Run(
                context.ParseResult.GetValueForOption(projectOption),
                context.ParseResult.GetValueForOption(targetOption)!,
                context.ParseResult.GetValueForOption(aotOption),
                context.ParseResult.GetValueForOption(bundleOption),
                context.ParseResult.GetValueForOption(updaterArtifactsOption));
        });

        return command;
    }

    internal static async Task<int> Run(
        DirectoryInfo? projectDir, string target, bool aot, bool bundle, bool updaterArtifacts)
    {
        var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");

        if (!File.Exists(configPath))
        {
            WriteError($"No carbon.json found in {workingDir}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        var effectiveConfigPath = WriteEffectiveConfig(config, workingDir);

        if (!ValidateProductionConfig(
                config, workingDir, target, bundle,
                updaterArtifacts || config.Bundle.Updater.CreateArtifacts,
                out var validationError))
        {
            WriteError(validationError);
            return 1;
        }

        if (!TryPrepareIcons(config, workingDir, out var icons, out var iconError))
        {
            WriteError(iconError);
            return 1;
        }

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

        if (target.Equals("osx-universal", StringComparison.OrdinalIgnoreCase))
        {
            if (!bundle)
            {
                WriteError("osx-universal produces an application bundle; add --bundle.");
                return 1;
            }
            return await BuildUniversalMac(
                config, workingDir, hostProject, frontendDist, effectiveConfigPath, icons, aot,
                updaterArtifacts);
        }

        Console.WriteLine("\n[Carbon] Step 2/2 - Embedding frontend and publishing .NET host...");
        var bundleProps = WriteBundleProps(
            workingDir, hostProject, frontendDist, effectiveConfigPath, target, icons, aot);
        if (!await PublishHost(hostProject, workingDir, target, bundleProps))
        {
            WriteError(".NET publish failed.");
            return 1;
        }

        var outputDir = Path.Combine(workingDir, "out", target);
        if (!HasEmbeddedAssetRuntime(hostProject, out var runtimeError))
        {
            Directory.Delete(outputDir, recursive: true);
            WriteError(runtimeError);
            return 1;
        }

        RemovePublishSymbols(outputDir);
        var validation = PublishOutputVerifier.Verify(outputDir, target, allowSidecars: aot);
        if (!validation.Success)
        {
            WriteError(validation.Error ?? "Publish output validation failed.");
            return 1;
        }

        var executable = validation.ExecutablePath;
        if (executable is null)
        {
            WriteError("Publish completed but no executable was produced.");
            return 1;
        }

        var artifact = Path.GetRelativePath(workingDir, executable);
        if (bundle)
        {
            var packageArtifact =
                target.StartsWith("osx", StringComparison.OrdinalIgnoreCase) ? await BundleMac(config, workingDir, target, icons) :
                target.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? await BundleWindows(config, workingDir, target, icons) :
                target.StartsWith("linux", StringComparison.OrdinalIgnoreCase) ? await BundleLinux(config, workingDir, target, icons) :
                null;

            if (packageArtifact is null)
            {
                WriteError($"Package artifact was not created for target {target}.");
                return 1;
            }

            artifact = packageArtifact;
        }

        if (!await CreateUpdaterArtifacts(
                config, workingDir, target, artifact,
                updaterArtifacts || config.Bundle.Updater.CreateArtifacts))
            return 1;

        await WriteBuildManifest(
            config, workingDir, target, artifact, bundle, aot,
            updaterArtifacts || config.Bundle.Updater.CreateArtifacts,
            icons);

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

    private static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }

    private static bool ValidateProductionConfig(
        CarbonConfig config,
        string workingDir,
        string target,
        bool bundle,
        bool updaterArtifacts,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(config.App.Name))
        {
            error = "app.name is required for production builds.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.App.Identifier) ||
            !config.App.Identifier.Contains('.') ||
            config.App.Identifier.Any(ch =>
                !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
        {
            error = "app.identifier must be a valid reverse-DNS identifier, e.g. com.example.app.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.App.Version))
        {
            error = "app.version is required for production builds.";
            return false;
        }

        if (bundle && string.IsNullOrWhiteSpace(config.Window.Icon))
            WriteWarning("No window.icon configured; platform packages will use fallback/default icons.");

        foreach (var resource in config.Bundle.Resources)
        {
            var path = Path.GetFullPath(Path.Combine(workingDir, resource));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                error = $"Configured bundle resource does not exist: {path}";
                return false;
            }
        }

        var duplicateExtensions = config.Bundle.FileAssociations
            .SelectMany(association => association.Extensions)
            .Select(extension => extension.TrimStart('.').ToLowerInvariant())
            .Where(extension => extension.Length > 0)
            .GroupBy(extension => extension)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateExtensions is not null)
        {
            error = $"Duplicate file association extension: {duplicateExtensions.Key}";
            return false;
        }

        var duplicateSchemes = config.Bundle.Protocols
            .SelectMany(protocol => protocol.Schemes)
            .Select(scheme => scheme.ToLowerInvariant())
            .Where(scheme => scheme.Length > 0)
            .GroupBy(scheme => scheme)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSchemes is not null)
        {
            error = $"Duplicate protocol scheme: {duplicateSchemes.Key}";
            return false;
        }

        if (bundle && target.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
        {
            var signingIdentity = config.Bundle.MacOS.SigningIdentity
                ?? Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY");
            var notarizationProfile = config.Bundle.MacOS.NotarizationProfile
                ?? Environment.GetEnvironmentVariable("APPLE_NOTARIZATION_PROFILE");

            if (!string.IsNullOrWhiteSpace(notarizationProfile) &&
                string.IsNullOrWhiteSpace(signingIdentity))
            {
                error = "macOS notarization requires bundle.macOS.signingIdentity or APPLE_SIGNING_IDENTITY.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(config.Bundle.MacOS.Entitlements))
            {
                var entitlements = Path.GetFullPath(
                    Path.Combine(workingDir, config.Bundle.MacOS.Entitlements));
                if (!File.Exists(entitlements))
                {
                    error = $"macOS entitlements file does not exist: {entitlements}";
                    return false;
                }
            }
        }

        if (bundle &&
            target.StartsWith("win", StringComparison.OrdinalIgnoreCase) &&
            config.Bundle.Windows.WebView2InstallMode == "offlineInstaller")
        {
            if (string.IsNullOrWhiteSpace(config.Bundle.Windows.WebView2InstallerPath))
            {
                error = "bundle.windows.webView2InstallerPath is required for offlineInstaller mode.";
                return false;
            }

            var webView2 = Path.GetFullPath(
                Path.Combine(workingDir, config.Bundle.Windows.WebView2InstallerPath));
            if (!File.Exists(webView2))
            {
                error = $"WebView2 offline installer not found: {webView2}";
                return false;
            }
        }

        if (config.Bundle.Updater.Active && config.Bundle.Updater.Endpoints.Count == 0)
        {
            error = "bundle.updater.active requires at least one updater endpoint.";
            return false;
        }

        if (config.Bundle.Updater.Active &&
            string.IsNullOrWhiteSpace(config.Bundle.Updater.PublicKey))
        {
            error = "bundle.updater.active requires bundle.updater.publicKey.";
            return false;
        }

        if (updaterArtifacts && config.Bundle.Updater.Endpoints.Count == 0)
            WriteWarning("Updater artifacts will be signed without an endpoint URL.");

        return true;
    }

    private static string WriteEffectiveConfig(CarbonConfig config, string workingDir)
    {
        var generatedDir = Path.Combine(workingDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var path = Path.Combine(generatedDir, "carbon.effective.json");
        ConfigLoader.Save(config, path);
        return path;
    }

    private static async Task<int> BuildUniversalMac(
        CarbonConfig config,
        string workingDir,
        string hostProject,
        string frontendDist,
        string configPath,
        IconAssets? icons,
        bool aot,
        bool updaterArtifacts)
    {
        if (!OperatingSystem.IsMacOS())
        {
            WriteError("osx-universal bundles can only be assembled on macOS.");
            return 1;
        }
        if (!ToolExists("clang"))
        {
            WriteError("osx-universal requires the Xcode command-line tools (clang).");
            return 1;
        }

        Console.WriteLine("\n[Carbon] Step 2/2 - Publishing arm64 and x64 .NET hosts...");
        var executables = new Dictionary<string, string>();
        foreach (var rid in new[] { "osx-arm64", "osx-x64" })
        {
            var props = WriteBundleProps(
                workingDir, hostProject, frontendDist, configPath, rid, icons, aot);
            if (!await PublishHost(hostProject, workingDir, rid, props))
            {
                WriteError($".NET publish failed for {rid}.");
                return 1;
            }
            var ridOutput = Path.Combine(workingDir, "out", rid);
            RemovePublishSymbols(ridOutput);
            var executable = FindExecutable(ridOutput, rid);
            if (executable is null)
            {
                WriteError($"Publish completed but no executable was produced for {rid}.");
                return 1;
            }
            executables[rid] = executable;
        }

        var artifact = await BundleUniversalMac(
            config, workingDir, executables["osx-arm64"], executables["osx-x64"], icons);
        if (artifact is null)
        {
            WriteError("Could not assemble the universal macOS bundle.");
            return 1;
        }
        if (!await CreateUpdaterArtifacts(
                config, workingDir, "osx-universal", artifact,
                updaterArtifacts || config.Bundle.Updater.CreateArtifacts))
            return 1;

        await WriteBuildManifest(
            config, workingDir, "osx-universal", artifact,
            bundled: true, aot,
            updaterArtifacts || config.Bundle.Updater.CreateArtifacts,
            icons);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[Carbon] Build complete -> {artifact}");
        Console.ResetColor();
        return 0;
    }

    private static async Task<string?> BundleUniversalMac(
        CarbonConfig config,
        string workingDir,
        string arm64Executable,
        string x64Executable,
        IconAssets? icons)
    {
        var outputDir = Path.Combine(workingDir, "out", "osx-universal");
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        var appName = string.IsNullOrWhiteSpace(config.App.Name) ? "Carbon App" : config.App.Name;
        var app = Path.Combine(outputDir, appName + ".app");
        var dmg = Path.Combine(outputDir, appName + ".dmg");
        var macos = Path.Combine(app, "Contents", "MacOS");
        var resources = Path.Combine(app, "Contents", "Resources");
        Directory.CreateDirectory(macos);
        Directory.CreateDirectory(resources);

        const string armPayload = "carbon-arm64";
        const string x64Payload = "carbon-x64";
        const string launcherName = "carbon-launcher";
        File.Copy(arm64Executable, Path.Combine(macos, armPayload), true);
        File.Copy(x64Executable, Path.Combine(macos, x64Payload), true);
        if (icons is not null) File.Copy(icons.Icns, Path.Combine(resources, "icon.icns"), true);
        if (!CopyBundleResources(config.Bundle.Resources, workingDir, resources)) return null;

        var launcherSource = Path.Combine(workingDir, "obj", "dotcarbon", "universal-launcher.c");
        Directory.CreateDirectory(Path.GetDirectoryName(launcherSource)!);
        await File.WriteAllTextAsync(launcherSource, """
            #include <libgen.h>
            #include <limits.h>
            #include <mach-o/dyld.h>
            #include <stdio.h>
            #include <unistd.h>

            int main(int argc, char **argv) {
                char executable[PATH_MAX];
                uint32_t size = sizeof(executable);
                if (_NSGetExecutablePath(executable, &size) != 0) return 1;
                char *directory = dirname(executable);
            #if defined(__arm64__)
                const char *payload = "carbon-arm64";
            #else
                const char *payload = "carbon-x64";
            #endif
                char target[PATH_MAX];
                if (snprintf(target, sizeof(target), "%s/%s", directory, payload) >= sizeof(target)) return 1;
                argv[0] = target;
                execv(target, argv);
                perror("DotCarbon launcher");
                return 1;
            }
            """);

        var launcher = Path.Combine(macos, launcherName);
        if (await RunProcessToCompletion(
                "clang", $"-arch arm64 -arch x86_64 \"{launcherSource}\" -o \"{launcher}\"",
                outputDir, "[universal]", ConsoleColor.Blue) != 0)
            return null;
        await RunProcessToCompletion(
            "chmod", $"+x \"{launcher}\" \"{Path.Combine(macos, armPayload)}\" \"{Path.Combine(macos, x64Payload)}\"",
            outputDir, "[pkg]", ConsoleColor.Blue);

        await File.WriteAllTextAsync(
            Path.Combine(app, "Contents", "Info.plist"),
            InfoPlist(config, launcherName, appName, icons is not null));
        if (!await SignMacApp(config, workingDir, outputDir, app)) return null;
        if (await RunProcessToCompletion(
                "hdiutil", $"create -volname \"{appName}\" -srcfolder \"{app}\" -ov -format UDZO \"{dmg}\"",
                outputDir, "[pkg]", ConsoleColor.Blue) != 0)
            return null;
        if (!await SignMacDmg(config, outputDir, dmg)) return null;
        if (!await NotarizeMacDmg(config, outputDir, dmg)) return null;
        return File.Exists(dmg) ? $"out/osx-universal/{appName}.dmg" : null;
    }

    private static async Task<string?> BundleMac(
        CarbonConfig config, string workingDir, string target, IconAssets? icons)
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
        var resources = Path.Combine(app, "Contents", "Resources");
        Directory.CreateDirectory(resources);

        foreach (var f in Directory.GetFiles(outDir))
            File.Copy(f, Path.Combine(macos, Path.GetFileName(f)), true);
        if (icons is not null)
            File.Copy(icons.Icns, Path.Combine(resources, "icon.icns"), true);
        if (!CopyBundleResources(config.Bundle.Resources, workingDir, resources))
            return null;

        await File.WriteAllTextAsync(
            Path.Combine(app, "Contents", "Info.plist"),
            InfoPlist(config, exeName, appName, icons is not null));
        await RunProcessToCompletion("chmod", $"+x \"{Path.Combine(macos, exeName)}\"", outDir, "[pkg]", ConsoleColor.Blue);

        if (!await SignMacApp(config, workingDir, outDir, app)) return null;

        if (await RunProcessToCompletion("hdiutil",
            $"create -volname \"{appName}\" -srcfolder \"{app}\" -ov -format UDZO \"{dmg}\"",
            outDir, "[pkg]", ConsoleColor.Blue) != 0)
            return null;

        if (!await SignMacDmg(config, outDir, dmg)) return null;
        if (!await NotarizeMacDmg(config, outDir, dmg)) return null;

        return File.Exists(dmg) ? $"out/{target}/{appName}.dmg" : null;
    }

    private static async Task<bool> SignMacApp(
        CarbonConfig config, string workingDir, string outputDir, string app)
    {
        var signingIdentity = GetMacSigningIdentity(config);
        if (string.IsNullOrWhiteSpace(signingIdentity)) return true;

        var entitlements = string.IsNullOrWhiteSpace(config.Bundle.MacOS.Entitlements)
            ? string.Empty
            : $" --entitlements \"{Path.GetFullPath(Path.Combine(workingDir, config.Bundle.MacOS.Entitlements))}\"";
        return await RunProcessToCompletion(
            "codesign",
            $"--force --deep --options runtime --timestamp --sign \"{signingIdentity}\"{entitlements} \"{app}\"",
            outputDir, "[sign]", ConsoleColor.DarkCyan) == 0;
    }

    private static async Task<bool> SignMacDmg(CarbonConfig config, string outputDir, string dmg)
    {
        var signingIdentity = GetMacSigningIdentity(config);
        if (string.IsNullOrWhiteSpace(signingIdentity)) return true;

        return await RunProcessToCompletion(
            "codesign",
            $"--force --timestamp --sign \"{signingIdentity}\" \"{dmg}\"",
            outputDir, "[sign]", ConsoleColor.DarkCyan) == 0;
    }

    private static string? GetMacSigningIdentity(CarbonConfig config) =>
        config.Bundle.MacOS.SigningIdentity
        ?? Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY");

    private static async Task<bool> NotarizeMacDmg(
        CarbonConfig config, string outputDir, string dmg)
    {
        var notarizationProfile = config.Bundle.MacOS.NotarizationProfile
            ?? Environment.GetEnvironmentVariable("APPLE_NOTARIZATION_PROFILE");
        if (string.IsNullOrWhiteSpace(notarizationProfile)) return true;

        if (await RunProcessToCompletion(
                "xcrun", $"notarytool submit \"{dmg}\" --keychain-profile \"{notarizationProfile}\" --wait",
                outputDir, "[notary]", ConsoleColor.DarkCyan) != 0)
            return false;
        return await RunProcessToCompletion(
            "xcrun", $"stapler staple \"{dmg}\"", outputDir, "[notary]", ConsoleColor.DarkCyan) == 0;
    }

    private static string InfoPlist(CarbonConfig config, string exeName, string appName, bool hasIcon)
    {
        var dict = new XElement("dict");
        AddPlist(dict, "CFBundleName", new XElement("string", appName));
        AddPlist(dict, "CFBundleDisplayName", new XElement("string", config.App.Name));
        AddPlist(dict, "CFBundleIdentifier", new XElement("string", config.App.Identifier));
        AddPlist(dict, "CFBundleVersion", new XElement("string", config.App.Version));
        AddPlist(dict, "CFBundleShortVersionString", new XElement("string", config.App.Version));
        AddPlist(dict, "CFBundleExecutable", new XElement("string", exeName));
        AddPlist(dict, "CFBundlePackageType", new XElement("string", "APPL"));
        AddPlist(dict, "CFBundleDevelopmentRegion", new XElement("string", "en"));
        AddPlist(dict, "LSMinimumSystemVersion", new XElement("string", config.Bundle.MacOS.MinimumSystemVersion));
        AddPlist(dict, "LSApplicationCategoryType", new XElement("string", MacCategory(config.Bundle.Category)));
        AddPlist(dict, "NSHighResolutionCapable", new XElement("true"));
        if (!string.IsNullOrWhiteSpace(config.Bundle.Copyright))
            AddPlist(dict, "NSHumanReadableCopyright", new XElement("string", config.Bundle.Copyright));
        if (hasIcon) AddPlist(dict, "CFBundleIconFile", new XElement("string", "icon.icns"));

        if (config.Bundle.FileAssociations.Count > 0)
        {
            var associations = new XElement("array");
            foreach (var association in config.Bundle.FileAssociations.Where(item => item.Extensions.Count > 0))
            {
                var item = new XElement("dict");
                AddPlist(item, "CFBundleTypeName", new XElement("string", association.Name));
                AddPlist(item, "CFBundleTypeRole", new XElement("string", association.Role));
                AddPlist(item, "LSHandlerRank", new XElement("string", "Owner"));
                AddPlist(item, "CFBundleTypeExtensions",
                    new XElement("array", association.Extensions.Select(extension =>
                        new XElement("string", extension.TrimStart('.')))));
                if (!string.IsNullOrWhiteSpace(association.MimeType))
                    AddPlist(item, "CFBundleTypeMIMETypes",
                        new XElement("array", new XElement("string", association.MimeType)));
                if (hasIcon) AddPlist(item, "CFBundleTypeIconFile", new XElement("string", "icon.icns"));
                associations.Add(item);
            }
            AddPlist(dict, "CFBundleDocumentTypes", associations);
        }

        if (config.Bundle.Protocols.Count > 0)
        {
            var protocols = new XElement("array");
            foreach (var protocol in config.Bundle.Protocols.Where(item => item.Schemes.Count > 0))
            {
                var item = new XElement("dict");
                AddPlist(item, "CFBundleURLName", new XElement("string", protocol.Name));
                AddPlist(item, "CFBundleURLSchemes",
                    new XElement("array", protocol.Schemes.Select(scheme => new XElement("string", scheme))));
                protocols.Add(item);
            }
            AddPlist(dict, "CFBundleURLTypes", protocols);
        }

        var document = new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), dict));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document;
    }

    private static void AddPlist(XElement dict, string key, XElement value) =>
        dict.Add(new XElement("key", key), value);

    private static string MacCategory(string category) => category switch
    {
        "Business" => "public.app-category.business",
        "DeveloperTool" => "public.app-category.developer-tools",
        "Education" => "public.app-category.education",
        "Entertainment" => "public.app-category.entertainment",
        "Finance" => "public.app-category.finance",
        "GraphicsAndDesign" => "public.app-category.graphics-design",
        "Music" => "public.app-category.music",
        "News" => "public.app-category.news",
        "Photography" => "public.app-category.photography",
        "Productivity" => "public.app-category.productivity",
        "SocialNetworking" => "public.app-category.social-networking",
        "Video" => "public.app-category.video",
        _ => "public.app-category.utilities",
    };

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(src, dst));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), true);
    }

    private static bool CopyBundleResources(IEnumerable<string> resources, string workingDir, string destination)
    {
        foreach (var configured in resources)
        {
            var source = Path.GetFullPath(Path.Combine(workingDir, configured));
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), true);
                continue;
            }
            if (Directory.Exists(source))
            {
                CopyDir(source, Path.Combine(destination, Path.GetFileName(source)));
                continue;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Carbon] Configured bundle resource does not exist: {source}");
            Console.ResetColor();
            return false;
        }
        return true;
    }

    private static async Task<string?> BundleWindows(
        CarbonConfig config, string workingDir, string target, IconAssets? icons)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir, "*.exe").FirstOrDefault();
        if (exe is null) return null;
        if (!ToolExists("wix"))
        {
            WriteError("Windows .msi packaging requires WiX. Install it with: dotnet tool install --global wix --version 4.*");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Windows .msi (WiX)...");
        var name = string.IsNullOrWhiteSpace(config.App.Name) ? Path.GetFileNameWithoutExtension(exe) : config.App.Name;
        var resourceDir = Path.Combine(outDir, "resources");
        if (config.Bundle.Resources.Count > 0)
        {
            Directory.CreateDirectory(resourceDir);
            if (!CopyBundleResources(config.Bundle.Resources, workingDir, resourceDir)) return null;
        }

        var thumbprint = config.Bundle.Windows.CertificateThumbprint
            ?? Environment.GetEnvironmentVariable("WINDOWS_CERTIFICATE_THUMBPRINT");
        if (!string.IsNullOrWhiteSpace(thumbprint) &&
            !await SignWindows(exe, thumbprint, config.Bundle.Windows.TimestampUrl, outDir))
            return null;

        var webView2 = await PrepareWebView2Installer(config.Bundle.Windows, workingDir);
        if (config.Bundle.Windows.WebView2InstallMode != "skip" && webView2 is null)
            return null;

        var wxs = Path.Combine(workingDir, "out", "installer.wxs");
        var msi = Path.Combine(workingDir, "out", name + ".msi");
        await File.WriteAllTextAsync(wxs, WindowsInstallerWxs(config, outDir, exe, webView2, icons?.Ico));
        if (await RunProcessToCompletion("wix", $"build \"{wxs}\" -o \"{msi}\"", outDir, "[pkg]", ConsoleColor.Blue) != 0)
        {
            WriteError("WiX failed to build the Windows .msi package.");
            return null;
        }
        if (!File.Exists(msi))
        {
            WriteError($"WiX completed but did not create the expected MSI: {msi}");
            return null;
        }
        if (File.Exists(msi) && !string.IsNullOrWhiteSpace(thumbprint) &&
            !await SignWindows(msi, thumbprint, config.Bundle.Windows.TimestampUrl, outDir))
            return null;
        return $"out/{name}.msi";
    }

    internal static string WindowsInstallerWxs(
        CarbonConfig config,
        string outDir,
        string exe,
        string? webView2,
        string? iconIco)
    {
        var name = string.IsNullOrWhiteSpace(config.App.Name) ? Path.GetFileNameWithoutExtension(exe) : config.App.Name;
        var xmlName = XmlEscape(name);
        var manufacturer = XmlEscape(config.Bundle.Publisher ?? config.App.Name);
        var componentIds = new List<string>();
        var directoryEntries = new System.Text.StringBuilder();
        var fileComponentIndex = 0;
        var directoryIndex = 0;
        AppendWindowsFileComponents(directoryEntries, outDir, componentIds, ref fileComponentIndex, ref directoryIndex, indent: "        ");
        WindowsRegistryEntries(directoryEntries, componentIds, config, exeName: Path.GetFileName(exe));

        var featureComponents = string.Concat(componentIds.Select(id => $"      <ComponentRef Id=\"{id}\" />\n"));
        var webView2Elements = webView2 is null ? string.Empty :
            $"    <Binary Id=\"WebView2Bootstrapper\" SourceFile=\"{XmlEscape(webView2)}\" />\n" +
            "    <CustomAction Id=\"InstallWebView2\" BinaryRef=\"WebView2Bootstrapper\" " +
            "ExeCommand=\"/silent /install\" Execute=\"deferred\" Impersonate=\"no\" Return=\"check\" />\n" +
            "    <InstallExecuteSequence>\n" +
            "      <Custom Action=\"InstallWebView2\" After=\"InstallFiles\" Condition=\"NOT Installed\" />\n" +
            "    </InstallExecuteSequence>\n";

        return
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n" +
            $"  <Package Name=\"{xmlName}\" Manufacturer=\"{manufacturer}\" Version=\"{MsiVersion(config.App.Version)}\" UpgradeCode=\"{StableGuid(config.App.Identifier)}\">\n" +
            "    <MajorUpgrade DowngradeErrorMessage=\"A newer version is already installed.\" />\n" +
            "    <MediaTemplate EmbedCab=\"yes\" />\n" +
            (iconIco is null ? string.Empty :
                $"    <Icon Id=\"AppIcon\" SourceFile=\"{XmlEscape(iconIco)}\" />\n" +
                "    <Property Id=\"ARPPRODUCTICON\" Value=\"AppIcon\" />\n") +
            webView2Elements +
            "    <StandardDirectory Id=\"ProgramFiles64Folder\">\n" +
            $"      <Directory Id=\"INSTALLFOLDER\" Name=\"{xmlName}\">\n" +
            directoryEntries +
            "      </Directory>\n" +
            "    </StandardDirectory>\n" +
            $"    <Feature Id=\"MainFeature\" Title=\"{xmlName}\" Level=\"1\">\n" +
            featureComponents +
            "    </Feature>\n" +
            "  </Package>\n</Wix>\n";
    }

    private static void AppendWindowsFileComponents(
        System.Text.StringBuilder xml,
        string directory,
        List<string> componentIds,
        ref int fileComponentIndex,
        ref int directoryIndex,
        string indent)
    {
        foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var componentId = "AppFile" + fileComponentIndex++;
            componentIds.Add(componentId);
            xml.AppendLine($"{indent}<Component Id=\"{componentId}\" Guid=\"*\">");
            xml.AppendLine($"{indent}  <File Id=\"{componentId}File\" Source=\"{XmlEscape(file)}\" KeyPath=\"yes\" />");
            xml.AppendLine($"{indent}</Component>");
        }

        foreach (var childDirectory in Directory.GetDirectories(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var directoryId = "AppDir" + directoryIndex++;
            xml.AppendLine($"{indent}<Directory Id=\"{directoryId}\" Name=\"{XmlEscape(Path.GetFileName(childDirectory))}\">");
            AppendWindowsFileComponents(xml, childDirectory, componentIds, ref fileComponentIndex, ref directoryIndex, indent + "  ");
            xml.AppendLine($"{indent}</Directory>");
        }
    }

    private static void WindowsRegistryEntries(
        System.Text.StringBuilder xml,
        List<string> componentIds,
        CarbonConfig config,
        string exeName)
    {
        exeName = XmlEscape(exeName);
        var componentIndex = 0;
        foreach (var association in config.Bundle.FileAssociations.Where(item => item.Extensions.Count > 0))
        {
            var componentId = $"FileAssociation{componentIndex++}";
            componentIds.Add(componentId);
            var progId = XmlEscape(config.App.Identifier + "." + association.Extensions[0].TrimStart('.'));
            xml.AppendLine($"        <Component Id=\"{componentId}\" Guid=\"*\">");
            var first = true;
            foreach (var extension in association.Extensions)
            {
                xml.AppendLine($"          <RegistryKey Root=\"HKCU\" Key=\"Software\\Classes\\.{XmlEscape(extension.TrimStart('.'))}\">");
                xml.AppendLine($"            <RegistryValue Type=\"string\" Value=\"{progId}\"{(first ? " KeyPath=\"yes\"" : string.Empty)} />");
                xml.AppendLine("          </RegistryKey>");
                first = false;
            }
            xml.AppendLine($"          <RegistryKey Root=\"HKCU\" Key=\"Software\\Classes\\{progId}\">");
            xml.AppendLine($"            <RegistryValue Type=\"string\" Value=\"{XmlEscape(association.Description)}\" />");
            xml.AppendLine("            <RegistryKey Key=\"shell\\open\\command\">");
            xml.AppendLine($"              <RegistryValue Type=\"string\" Value=\"&quot;[INSTALLFOLDER]{exeName}&quot; &quot;%1&quot;\" />");
            xml.AppendLine("            </RegistryKey>");
            xml.AppendLine("          </RegistryKey>");
            xml.AppendLine("        </Component>");
        }

        foreach (var protocol in config.Bundle.Protocols.Where(item => item.Schemes.Count > 0))
        {
            foreach (var configuredScheme in protocol.Schemes)
            {
                var componentId = $"Protocol{componentIndex++}";
                componentIds.Add(componentId);
                var scheme = XmlEscape(configuredScheme);
                xml.AppendLine($"        <Component Id=\"{componentId}\" Guid=\"*\">");
                xml.AppendLine($"          <RegistryKey Root=\"HKCU\" Key=\"Software\\Classes\\{scheme}\">");
                xml.AppendLine($"            <RegistryValue Type=\"string\" Value=\"URL:{XmlEscape(protocol.Name)}\" KeyPath=\"yes\" />");
                xml.AppendLine("            <RegistryValue Name=\"URL Protocol\" Type=\"string\" Value=\"\" />");
                xml.AppendLine("            <RegistryKey Key=\"shell\\open\\command\">");
                xml.AppendLine($"              <RegistryValue Type=\"string\" Value=\"&quot;[INSTALLFOLDER]{exeName}&quot; &quot;%1&quot;\" />");
                xml.AppendLine("            </RegistryKey>");
                xml.AppendLine("          </RegistryKey>");
                xml.AppendLine("        </Component>");
            }
        }
    }

    private static string XmlEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static async Task<string?> PrepareWebView2Installer(
        WindowsBundleConfig config, string workingDir)
    {
        if (config.WebView2InstallMode == "skip") return null;
        if (config.WebView2InstallMode == "offlineInstaller")
        {
            if (string.IsNullOrWhiteSpace(config.WebView2InstallerPath))
            {
                WriteError("windows.webView2InstallerPath is required for offlineInstaller mode.");
                return null;
            }
            var configured = Path.GetFullPath(Path.Combine(workingDir, config.WebView2InstallerPath));
            if (!File.Exists(configured))
            {
                WriteError($"WebView2 offline installer not found: {configured}");
                return null;
            }
            return configured;
        }

        if (config.WebView2InstallMode != "downloadBootstrapper")
        {
            WriteError($"Unknown WebView2 install mode: {config.WebView2InstallMode}");
            return null;
        }

        var supportDir = Path.Combine(workingDir, "out", "support");
        Directory.CreateDirectory(supportDir);
        var bootstrapper = Path.Combine(supportDir, "MicrosoftEdgeWebview2Setup.exe");
        if (File.Exists(bootstrapper)) return bootstrapper;

        Console.WriteLine("[Carbon] Downloading Microsoft WebView2 Evergreen bootstrapper...");
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            await File.WriteAllBytesAsync(bootstrapper, bytes);
            return bootstrapper;
        }
        catch (Exception ex)
        {
            WriteError($"Could not download the WebView2 bootstrapper: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> SignWindows(
        string artifact, string thumbprint, string timestampUrl, string workingDir)
    {
        if (!ToolExists("signtool"))
        {
            WriteError("Windows signing is configured but signtool is not available.");
            return false;
        }
        return await RunProcessToCompletion(
            "signtool",
            $"sign /sha1 \"{thumbprint}\" /fd SHA256 /tr \"{timestampUrl}\" /td SHA256 \"{artifact}\"",
            workingDir, "[sign]", ConsoleColor.DarkCyan) == 0;
    }

    private static async Task<bool> CreateUpdaterArtifacts(
        CarbonConfig config, string workingDir, string target, string artifactRelative, bool enabled)
    {
        if (!enabled) return true;

        var artifact = Path.GetFullPath(Path.Combine(workingDir, artifactRelative));
        if (!File.Exists(artifact))
        {
            WriteError($"Updater artifact does not exist: {artifact}");
            return false;
        }

        var configuredKey = Environment.GetEnvironmentVariable("CARBON_UPDATER_PRIVATE_KEY");
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            WriteError("bundle.updater.createArtifacts requires CARBON_UPDATER_PRIVATE_KEY (PEM text or a key file path).");
            return false;
        }

        try
        {
            var pem = File.Exists(configuredKey)
                ? await File.ReadAllTextAsync(configuredKey)
                : configuredKey.Replace("\\n", "\n", StringComparison.Ordinal);
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);

            var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            if (!string.IsNullOrWhiteSpace(config.Bundle.Updater.PublicKey) &&
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(config.Bundle.Updater.PublicKey),
                    Convert.FromBase64String(publicKey)))
            {
                WriteError("The updater private key does not match bundle.updater.publicKey.");
                return false;
            }

            var bytes = await File.ReadAllBytesAsync(artifact);
            var signature = Convert.ToBase64String(key.SignData(
                bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            var signaturePath = artifact + ".sig";
            await File.WriteAllTextAsync(signaturePath, signature + Environment.NewLine);

            var endpoint = config.Bundle.Updater.Endpoints.FirstOrDefault() ?? string.Empty;
            endpoint = endpoint
                .Replace("{{target}}", target, StringComparison.Ordinal)
                .Replace("{{version}}", config.App.Version, StringComparison.Ordinal)
                .Replace("{{artifact}}", Uri.EscapeDataString(Path.GetFileName(artifact)), StringComparison.Ordinal);
            var metadata = new
            {
                version = config.App.Version,
                target,
                url = endpoint,
                artifact = Path.GetFileName(artifact),
                signature,
                publicKey,
                algorithm = "ECDSA_P256_SHA256",
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                size = bytes.LongLength,
            };
            var metadataPath = artifact + ".update.json";
            await File.WriteAllTextAsync(metadataPath,
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine($"[Carbon] Signed updater artifacts -> {Path.GetRelativePath(workingDir, signaturePath)}, " +
                              Path.GetRelativePath(workingDir, metadataPath));
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            WriteError($"Could not sign updater artifact: {ex.Message}");
            return false;
        }
    }

    private static async Task<string?> BundleLinux(
        CarbonConfig config, string workingDir, string target, IconAssets? icons)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var outDir = Path.Combine(workingDir, "out", target);
        var exe = Directory.GetFiles(outDir).FirstOrDefault(IsUnixExecutable);
        if (exe is null) return null;

        var exeName = Path.GetFileName(exe);
        var name = string.IsNullOrWhiteSpace(config.App.Name) ? exeName : config.App.Name;
        var slug = LinuxSlug(name);

        var formats = config.Bundle.Linux.Formats
            .Select(format => format.Trim().ToLowerInvariant())
            .Where(format => format is "appimage" or "deb" or "rpm")
            .Distinct()
            .ToList();
        if (formats.Count == 0) formats.Add("appimage");

        var produced = new List<string>();
        string? primary = null;

        // Each format is isolated: a failure in one (e.g. appimagetool missing/incompatible) must not
        // abort the others. Report and continue.
        async Task TryBuild(string label, Func<Task<string?>> build, bool isPrimary = false)
        {
            try
            {
                var artifact = await build();
                if (artifact is not null)
                {
                    produced.Add(artifact);
                    if (isPrimary) primary ??= artifact;
                }
            }
            catch (Exception ex)
            {
                WriteWarning($"Skipping .{label}: {ex.Message}");
            }
        }

        if (formats.Contains("appimage"))
            await TryBuild("AppImage", () => BuildAppImage(config, workingDir, outDir, target, name, slug, exeName, icons), isPrimary: true);
        if (formats.Contains("deb"))
            await TryBuild("deb", () => BuildDeb(config, workingDir, outDir, target, name, slug, exeName, icons));
        if (formats.Contains("rpm"))
            await TryBuild("rpm", () => BuildRpm(config, workingDir, outDir, target, name, slug, exeName, icons));

        primary ??= produced.FirstOrDefault();
        if (produced.Count > 0)
            Console.WriteLine($"[Carbon] Linux packages: {string.Join(", ", produced.Select(Path.GetFileName))}");
        return primary is null ? null : $"out/{target}/{Path.GetFileName(primary)}";
    }

    private static async Task<string?> BuildAppImage(
        CarbonConfig config, string workingDir, string outDir, string target,
        string name, string slug, string exeName, IconAssets? icons)
    {
        var tool = await EnsureAppImageTool(workingDir, target);
        if (tool is null)
        {
            WriteWarning("Skipping .AppImage: appimagetool is unavailable (the binary is still in the output).");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Linux .AppImage...");
        var appdir = Path.Combine(outDir, "AppDir");
        if (Directory.Exists(appdir)) Directory.Delete(appdir, true);
        var bin = Path.Combine(appdir, "usr", "bin");
        Directory.CreateDirectory(bin);
        var resourceDir = Path.Combine(appdir, "usr", "lib", slug);
        Directory.CreateDirectory(resourceDir);

        foreach (var f in Directory.GetFiles(outDir)) File.Copy(f, Path.Combine(bin, Path.GetFileName(f)), true);
        if (!CopyBundleResources(config.Bundle.Resources, workingDir, resourceDir)) return null;

        await File.WriteAllTextAsync(Path.Combine(appdir, "AppRun"),
            "#!/bin/sh\nHERE=\"$(dirname \"$(readlink -f \"$0\")\")\"\n" +
            "export CARBON_RESOURCE_DIR=\"$HERE/usr/lib/" + slug + "\"\n" +
            "cd \"$HERE/usr/bin\"\nexec \"./" + exeName + "\" \"$@\"\n");
        await File.WriteAllTextAsync(Path.Combine(appdir, slug + ".desktop"),
            BuildDesktopEntry(config, name, slug, exeName));
        WriteLinuxMimePackages(config, Path.Combine(appdir, "usr", "share", "mime", "packages"), slug);

        if (icons is not null)
        {
            File.Copy(icons.Linux512, Path.Combine(appdir, slug + ".png"), true);
            CopyLinuxIcons(icons, Path.Combine(appdir, "usr", "share", "icons", "hicolor"), slug);
        }
        else
        {
            await File.WriteAllBytesAsync(Path.Combine(appdir, slug + ".png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        }

        await RunProcessToCompletion("chmod", $"+x \"{Path.Combine(appdir, "AppRun")}\" \"{Path.Combine(bin, exeName)}\"", outDir, "[pkg]", ConsoleColor.Blue);
        Environment.SetEnvironmentVariable("ARCH", AppImageArch(target));
        Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", "1");
        var appimage = Path.Combine(outDir, name + ".AppImage");
        await RunProcessToCompletion(tool, $"\"{appdir}\" \"{appimage}\"", outDir, "[pkg]", ConsoleColor.Blue);
        return File.Exists(appimage) ? appimage : null;
    }

    private static async Task<string?> BuildDeb(
        CarbonConfig config, string workingDir, string outDir, string target,
        string name, string slug, string exeName, IconAssets? icons)
    {
        if (!ToolExists("dpkg-deb"))
        {
            WriteWarning("Skipping .deb: dpkg-deb not found (install the dpkg package).");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Linux .deb...");
        var root = Path.Combine(outDir, "deb-root");
        if (!await StageFhsPayload(config, workingDir, outDir, root, name, slug, exeName, icons)) return null;

        var arch = DebArch(target);
        var version = config.App.Version;
        var debianDir = Path.Combine(root, "DEBIAN");
        Directory.CreateDirectory(debianDir);
        var depends = config.Bundle.Linux.Depends.Count > 0
            ? "Depends: " + string.Join(", ", config.Bundle.Linux.Depends) + "\n"
            : string.Empty;
        var control =
            $"Package: {slug}\n" +
            $"Version: {version}\n" +
            $"Section: {config.Bundle.Linux.Section}\n" +
            $"Priority: {config.Bundle.Linux.Priority}\n" +
            $"Architecture: {arch}\n" +
            $"Maintainer: {LinuxMaintainer(config)}\n" +
            depends +
            $"Description: {name}\n";
        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"), control);

        var deb = Path.Combine(outDir, $"{slug}_{version}_{arch}.deb");
        if (File.Exists(deb)) File.Delete(deb);
        await RunProcessToCompletion("dpkg-deb",
            $"--build --root-owner-group \"{root}\" \"{deb}\"", outDir, "[pkg]", ConsoleColor.Blue);
        return File.Exists(deb) ? deb : null;
    }

    private static async Task<string?> BuildRpm(
        CarbonConfig config, string workingDir, string outDir, string target,
        string name, string slug, string exeName, IconAssets? icons)
    {
        if (!ToolExists("rpmbuild"))
        {
            WriteWarning("Skipping .rpm: rpmbuild not found (install the rpm/rpm-build package).");
            return null;
        }

        Console.WriteLine("\n[Carbon] Packaging Linux .rpm...");
        var stage = Path.Combine(outDir, "rpm-stage");
        if (!await StageFhsPayload(config, workingDir, outDir, stage, name, slug, exeName, icons)) return null;

        var arch = RpmArch(target);
        var version = config.App.Version.Replace('-', '_');
        var topDir = Path.Combine(outDir, "rpmbuild");
        if (Directory.Exists(topDir)) Directory.Delete(topDir, true);
        Directory.CreateDirectory(Path.Combine(topDir, "SPECS"));

        var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
            .Select(file => "/" + Path.GetRelativePath(stage, file).Replace('\\', '/'))
            .Select(path => path.Contains(' ') ? $"\"{path}\"" : path);
        var requires = config.Bundle.Linux.Depends.Count > 0
            ? "Requires: " + string.Join(", ", config.Bundle.Linux.Depends) + "\n"
            : string.Empty;
        var spec =
            $"Name: {slug}\n" +
            $"Version: {version}\n" +
            "Release: 1\n" +
            $"Summary: {name}\n" +
            $"License: {config.Bundle.Linux.License}\n" +
            $"BuildArch: {arch}\n" +
            requires +
            "%global __os_install_post %{nil}\n" +
            "%description\n" + name + "\n" +
            "%install\nrm -rf %{buildroot}\nmkdir -p %{buildroot}\ncp -a %{_stage}/. %{buildroot}/\n" +
            "%files\n" + string.Join("\n", files) + "\n";
        var specPath = Path.Combine(topDir, "SPECS", slug + ".spec");
        await File.WriteAllTextAsync(specPath, spec);

        await RunProcessToCompletion("rpmbuild",
            $"-bb --define \"_topdir {topDir}\" --define \"_stage {stage}\" --target {arch} \"{specPath}\"",
            outDir, "[pkg]", ConsoleColor.Blue);

        var rpmsDir = Path.Combine(topDir, "RPMS");
        var built = Directory.Exists(rpmsDir)
            ? Directory.GetFiles(rpmsDir, "*.rpm", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (built is null) return null;
        var dest = Path.Combine(outDir, Path.GetFileName(built));
        File.Copy(built, dest, true);
        return dest;
    }

    // Builds the shared FHS payload (/usr/lib/<slug>, a /usr/bin wrapper, .desktop, icons,
    // mime) that deb and rpm install into the system root.
    private static async Task<bool> StageFhsPayload(
        CarbonConfig config, string workingDir, string outDir, string root,
        string name, string slug, string exeName, IconAssets? icons)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var libDir = Path.Combine(root, "usr", "lib", slug);
        Directory.CreateDirectory(libDir);
        foreach (var f in Directory.GetFiles(outDir)) File.Copy(f, Path.Combine(libDir, Path.GetFileName(f)), true);
        if (!CopyBundleResources(config.Bundle.Resources, workingDir, libDir)) return false;

        var binDir = Path.Combine(root, "usr", "bin");
        Directory.CreateDirectory(binDir);
        var wrapper = Path.Combine(binDir, slug);
        await File.WriteAllTextAsync(wrapper,
            $"#!/bin/sh\nexport CARBON_RESOURCE_DIR=\"/usr/lib/{slug}\"\nexec \"/usr/lib/{slug}/{exeName}\" \"$@\"\n");

        var appsDir = Path.Combine(root, "usr", "share", "applications");
        Directory.CreateDirectory(appsDir);
        await File.WriteAllTextAsync(Path.Combine(appsDir, slug + ".desktop"),
            BuildDesktopEntry(config, name, slug, slug));

        WriteLinuxMimePackages(config, Path.Combine(root, "usr", "share", "mime", "packages"), slug);
        if (icons is not null)
            CopyLinuxIcons(icons, Path.Combine(root, "usr", "share", "icons", "hicolor"), slug);

        await RunProcessToCompletion("chmod",
            $"+x \"{wrapper}\" \"{Path.Combine(libDir, exeName)}\"", outDir, "[pkg]", ConsoleColor.Blue);
        return true;
    }

    private static string BuildDesktopEntry(CarbonConfig config, string name, string iconSlug, string execCommand)
    {
        var mimeTypes = config.Bundle.FileAssociations
            .Select(item => item.MimeType)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Concat(config.Bundle.Protocols.SelectMany(protocol => protocol.Schemes)
                .Select(scheme => "x-scheme-handler/" + scheme))
            .ToList();
        return "[Desktop Entry]\nType=Application\nName=" + name + "\nExec=" + execCommand + " %U\nIcon=" + iconSlug +
            "\nCategories=" + config.Bundle.Linux.Category + ";\n" +
            (mimeTypes.Count > 0 ? "MimeType=" + string.Join(';', mimeTypes) + ";\n" : string.Empty);
    }

    private static void WriteLinuxMimePackages(CarbonConfig config, string mimeDir, string slug)
    {
        var associations = config.Bundle.FileAssociations
            .Where(item => !string.IsNullOrWhiteSpace(item.MimeType) && item.Extensions.Count > 0)
            .ToList();
        if (associations.Count == 0) return;

        var mimeRoot = new XElement("mime-info",
            new XAttribute("xmlns", "http://www.freedesktop.org/standards/shared-mime-info"));
        foreach (var association in associations)
        {
            mimeRoot.Add(new XElement("mime-type",
                new XAttribute("type", association.MimeType!),
                new XElement("comment", string.IsNullOrWhiteSpace(association.Description)
                    ? association.Name
                    : association.Description),
                association.Extensions.Select(extension => new XElement("glob",
                    new XAttribute("pattern", "*." + extension.TrimStart('.'))))));
        }
        Directory.CreateDirectory(mimeDir);
        File.WriteAllText(Path.Combine(mimeDir, slug + ".xml"), mimeRoot.ToString());
    }

    private static void CopyLinuxIcons(IconAssets icons, string hicolorDir, string slug)
    {
        foreach (var icon in Directory.GetFiles(icons.LinuxDirectory, "*.png"))
        {
            var size = Path.GetFileNameWithoutExtension(icon);
            var iconDir = Path.Combine(hicolorDir, size, "apps");
            Directory.CreateDirectory(iconDir);
            File.Copy(icon, Path.Combine(iconDir, slug + ".png"), true);
        }
    }

    private static string LinuxSlug(string name) =>
        new(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());

    private static string LinuxMaintainer(CarbonConfig config) =>
        !string.IsNullOrWhiteSpace(config.Bundle.Linux.Maintainer) ? config.Bundle.Linux.Maintainer! :
        !string.IsNullOrWhiteSpace(config.Bundle.Publisher) ? config.Bundle.Publisher! :
        config.App.Name;

    private static string AppImageArch(string target) => target.EndsWith("arm64") ? "aarch64" : "x86_64";
    private static string DebArch(string target) => target.EndsWith("arm64") ? "arm64" : "amd64";
    private static string RpmArch(string target) => target.EndsWith("arm64") ? "aarch64" : "x86_64";

    private static async Task<string?> EnsureAppImageTool(string workingDir, string target)
    {
        if (ToolExists("appimagetool")) return "appimagetool";
        // appimagetool ships per-arch; the tool binary must match the *host* building it, which for a
        // given target is the same arch we package for (aarch64 vs x86_64).
        var arch = AppImageArch(target);
        var local = Path.Combine(workingDir, "out", $"appimagetool-{arch}");
        if (!File.Exists(local))
        {
            Console.WriteLine($"[Carbon] Downloading appimagetool ({arch})...");
            var code = await RunProcessToCompletion("curl",
                $"-fsSL -o \"{local}\" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-{arch}.AppImage",
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

    private static bool TryPrepareIcons(
        CarbonConfig config,
        string workingDir,
        out IconAssets? icons,
        out string error)
    {
        icons = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(config.Window.Icon)) return true;

        var source = Path.GetFullPath(Path.Combine(workingDir, config.Window.Icon));
        if (!File.Exists(source))
        {
            error = $"Configured icon does not exist: {source}";
            return false;
        }

        var outputDir = Path.GetDirectoryName(source)!;
        var ico = Path.Combine(outputDir, "icon.ico");
        var icns = Path.Combine(outputDir, "icon.icns");
        var linuxDir = Path.Combine(outputDir, "linux");
        var linux512 = Path.Combine(linuxDir, "512x512.png");
        var generated = new[] { ico, icns, linux512 };
        var sourceTime = File.GetLastWriteTimeUtc(source);
        if (generated.Any(path => !File.Exists(path) || File.GetLastWriteTimeUtc(path) < sourceTime) &&
            !IconCommand.Generate(source, outputDir, out error))
            return false;

        icons = new IconAssets(source, ico, icns, linuxDir, linux512);
        return true;
    }

    private sealed record IconAssets(
        string Source,
        string Ico,
        string Icns,
        string LinuxDirectory,
        string Linux512);

    internal static async Task<bool> BuildFrontend(CarbonConfig config, string workingDir)
    {
        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        var distParent = Path.GetDirectoryName(frontendDist) ?? workingDir;
        var packageJsonDir = FindPackageJson(distParent);
        if (packageJsonDir is null &&
            string.IsNullOrWhiteSpace(config.Build.BuildCommand) &&
            File.Exists(Path.Combine(frontendDist, "index.html")))
        {
            Console.WriteLine($"[Carbon] Frontend dist already present -> {Path.GetRelativePath(workingDir, frontendDist)}");
            return true;
        }

        var buildCommand = string.IsNullOrWhiteSpace(config.Build.BuildCommand)
            ? config.Build.DevCommand.Replace("run dev", "run build").Replace(" dev", " build")
            : config.Build.BuildCommand;

        var parts = buildCommand.Split(' ', 2);
        var cmd = parts[0];
        var args = parts.Length > 1 ? parts[1] : "build";

        var uiDir = packageJsonDir ?? workingDir;

        var exitCode = await RunProcessToCompletion(cmd, args, uiDir, "[UI]", ConsoleColor.Green);
        return exitCode == 0;
    }

    private static string WriteBundleProps(
        string workingDir,
        string hostProject,
        string frontendDist,
        string configPath,
        string target,
        IconAssets? icons,
        bool aot)
    {
        var generatedDir = Path.Combine(workingDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var propsPath = Path.Combine(generatedDir, "DotCarbon.Bundle.props");
        var condition = $"'$(MSBuildProjectFullPath)' == '{hostProject.Replace("'", "%27")}'";
        var properties = new XElement("PropertyGroup",
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
            new XElement("StripSymbols", aot.ToString().ToLowerInvariant()));
        if (icons is not null && target.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            properties.Add(new XElement("ApplicationIcon", icons.Ico));

        var document = new XDocument(
            new XElement("Project",
                properties,
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

        var restoreArgs = $"restore \"{hostProject}\" " +
                          $"--runtime {target} " +
                          "-p:NuGetAudit=false";
        if (await RunProcessToCompletion(
                "dotnet", restoreArgs, workingDir, "[C#]", ConsoleColor.Magenta) != 0)
            return false;

        var args = $"publish \"{hostProject}\" " +
                   $"--runtime {target} " +
                   $"--configuration Release " +
                   $"--output \"{outputDir}\" " +
                   $"--self-contained true " +
                   $"--no-restore " +
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

    private static async Task WriteBuildManifest(
        CarbonConfig config,
        string workingDir,
        string target,
        string artifactRelative,
        bool bundled,
        bool aot,
        bool updaterArtifacts,
        IconAssets? icons)
    {
        var artifact = Path.GetFullPath(Path.Combine(workingDir, artifactRelative));
        if (!File.Exists(artifact)) return;

        var manifestDir = Path.Combine(workingDir, "out", "manifests");
        Directory.CreateDirectory(manifestDir);
        var manifestPath = Path.Combine(manifestDir, target + ".build.json");
        var artifactInfo = new FileInfo(artifact);
        var signature = artifact + ".sig";
        var updateMetadata = artifact + ".update.json";

        var manifest = new
        {
            format = "dotcarbon-build-manifest-v1",
            builtAt = DateTimeOffset.UtcNow,
            app = new
            {
                config.App.Name,
                config.App.Version,
                config.App.Identifier,
            },
            target,
            artifact = new
            {
                path = Path.GetRelativePath(workingDir, artifact),
                fileName = Path.GetFileName(artifact),
                size = artifactInfo.Length,
                sha256 = await Sha256FileAsync(artifact),
                packageType = GetArtifactKind(artifact),
                bundled,
                aot,
            },
            icons = icons is null ? null : new
            {
                source = Path.GetRelativePath(workingDir, icons.Source),
                ico = Path.GetRelativePath(workingDir, icons.Ico),
                icns = Path.GetRelativePath(workingDir, icons.Icns),
                linux = Path.GetRelativePath(workingDir, icons.LinuxDirectory),
            },
            bundle = new
            {
                resources = config.Bundle.Resources,
                fileAssociations = config.Bundle.FileAssociations.Count,
                protocols = config.Bundle.Protocols.SelectMany(protocol => protocol.Schemes).ToArray(),
                updaterArtifacts,
            },
            updater = new
            {
                signature = File.Exists(signature)
                    ? Path.GetRelativePath(workingDir, signature)
                    : null,
                metadata = File.Exists(updateMetadata)
                    ? Path.GetRelativePath(workingDir, updateMetadata)
                    : null,
            },
        };

        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }) +
            Environment.NewLine);
        Console.WriteLine($"[Carbon] Build manifest -> {Path.GetRelativePath(workingDir, manifestPath)}");
    }

    private static string GetArtifactKind(string artifact)
    {
        var extension = Path.GetExtension(artifact).ToLowerInvariant();
        return extension switch
        {
            ".dmg" => "macos-dmg",
            ".msi" => "windows-msi",
            ".appimage" => "linux-appimage",
            ".exe" => "windows-executable",
            _ => "executable",
        };
    }

    private static async Task<string> Sha256FileAsync(string path)
    {
        // A freshly created macOS .dmg can still be held by hdiutil (or the OS mounting/indexing it)
        // for a moment after creation, so opening it to hash may transiently fail with a sharing
        // violation. Retry with a short backoff rather than crashing the whole bundle.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(250);
            }
        }
    }

    private static bool HasEmbeddedAssetRuntime(string hostProject, out string error)
    {
        error = string.Empty;
        var assetsPath = Path.Combine(Path.GetDirectoryName(hostProject)!, "obj", "project.assets.json");
        if (!File.Exists(assetsPath)) return true;

        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        if (!assets.RootElement.TryGetProperty("libraries", out var libraries)) return true;

        foreach (var library in libraries.EnumerateObject())
        {
            if (!library.Name.StartsWith("DotCarbon.Core/", StringComparison.OrdinalIgnoreCase)) continue;
            if (library.Value.TryGetProperty("type", out var type) && type.GetString() == "project") return true;
            if (!library.Value.TryGetProperty("path", out var pathElement)) break;
            if (!assets.RootElement.TryGetProperty("packageFolders", out var folders)) break;

            var packageRoot = folders.EnumerateObject().Select(folder => folder.Name).FirstOrDefault();
            if (packageRoot is null) break;
            var packagePath = Path.Combine(packageRoot, pathElement.GetString()!);
            var coreAssembly = Directory.EnumerateFiles(packagePath, "DotCarbon.Core.dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (coreAssembly is null) break;

            using var stream = File.OpenRead(coreAssembly);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var supportsEmbeddedAssets = metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Any(definition =>
                    metadata.GetString(definition.Namespace) == "DotCarbon.Core.Host" &&
                    metadata.GetString(definition.Name) == "EmbeddedAssetStore");

            if (supportsEmbeddedAssets) return true;

            var version = library.Name[(library.Name.IndexOf('/') + 1)..];
            error = $"The app resolved DotCarbon.Core {version}, which cannot load embedded frontend assets. " +
                    "Update DotCarbon.Core to the same release as the Carbon CLI, then rebuild.";
            return false;
        }

        return true;
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

    internal static async Task<int> RunProcessToCompletion(
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

    internal static string GetDefaultTarget()
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
}
