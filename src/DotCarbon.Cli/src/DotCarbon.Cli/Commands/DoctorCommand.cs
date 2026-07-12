using System.CommandLine;
using System.Text.Json;
using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon doctor</c> — reports the plugin platform matrix and warns when the app references
/// plugins that don't support a mobile target.
/// </summary>
public static class DoctorCommand
{
    public static Command Build()
    {
        var command = new Command("doctor", "Check plugin platform compatibility across desktop/android/ios");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        command.AddOption(project);
        command.SetHandler(context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                WriteColor($"[Carbon] No carbon.json found in {workingDir}", ConsoleColor.Red);
                context.ExitCode = 1;
                return;
            }
            context.ExitCode = Run(ConfigLoader.Load(configPath), workingDir);
        });

        command.AddCommand(SigningSubcommand());
        return command;
    }

    private static Command SigningSubcommand()
    {
        var cmd = new Command("signing", "Check signing/distribution readiness for every platform");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        cmd.AddOption(project);
        cmd.SetHandler(context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                WriteColor($"[Carbon] No carbon.json found in {workingDir}", ConsoleColor.Red);
                context.ExitCode = 1;
                return;
            }
            context.ExitCode = RunSigning(ConfigLoader.Load(configPath));
        });
        return cmd;
    }

    private static int RunSigning(CarbonConfig config)
    {
        WriteColor("\n⚡ Carbon doctor — signing\n", ConsoleColor.Cyan);

        var mac = config.Bundle.MacOS;
        var win = config.Bundle.Windows;
        var updater = config.Bundle.Updater;
        var android = config.Bundle.Android.Signing;
        var ios = config.Bundle.Ios.Signing;

        Console.WriteLine("Desktop:");
        Line("macOS codesign",
            !string.IsNullOrWhiteSpace(mac.SigningIdentity) || SigningSupport.HasEnv(SigningSupport.MacIdentityEnv),
            $"set bundle.macOS.signingIdentity or {SigningSupport.MacIdentityEnv}");
        Line("macOS notarization",
            !string.IsNullOrWhiteSpace(mac.NotarizationProfile) || SigningSupport.HasEnv(SigningSupport.MacNotarizationEnv),
            $"set bundle.macOS.notarizationProfile or {SigningSupport.MacNotarizationEnv}");
        Line("Windows signtool",
            !string.IsNullOrWhiteSpace(win.CertificateThumbprint),
            "set bundle.windows.certificateThumbprint (cert imported into the machine store)");
        Line("Updater signing",
            SigningSupport.HasEnv(SigningSupport.UpdaterKeyEnv),
            $"set {SigningSupport.UpdaterKeyEnv} (generate keys with `carbon signer`)");

        Console.WriteLine("\nAndroid:");
        Line("Keystore",
            !string.IsNullOrWhiteSpace(android.Keystore) && !string.IsNullOrWhiteSpace(android.KeyAlias),
            "set bundle.android.signing.keystore + keyAlias");
        Line("Keystore password (env)",
            SigningSupport.HasEnv(SigningSupport.AndroidKeystorePasswordEnv),
            $"set {SigningSupport.AndroidKeystorePasswordEnv} (and {SigningSupport.AndroidKeyPasswordEnv} if different)");

        Console.WriteLine("\niOS:");
        Line("Identity", !string.IsNullOrWhiteSpace(ios.Identity), "set bundle.ios.signing.identity");
        Line("Provisioning profile", !string.IsNullOrWhiteSpace(ios.ProvisioningProfile), "set bundle.ios.signing.provisioningProfile");
        Line("Development team", !string.IsNullOrWhiteSpace(config.Bundle.Ios.DevelopmentTeam), "set bundle.ios.developmentTeam");

        Console.WriteLine();
        Console.WriteLine("Secrets come from the environment (CI: repository/environment secrets); certs/profiles");
        Console.WriteLine("are installed into the build machine's keychain/store, not committed to carbon.json.");
        return 0;
    }

    private static void Line(string label, bool ok, string hint)
    {
        Console.Write($"  {label,-24} ");
        WriteColor(ok ? "✓" : "✗", ok ? ConsoleColor.Green : ConsoleColor.Yellow, newline: false);
        Console.WriteLine(ok ? " configured" : $" — {hint}");
    }

    private static int Run(CarbonConfig config, string workingDir)
    {
        WriteColor("\n⚡ Carbon doctor\n", ConsoleColor.Cyan);

        var errors = new List<string>();
        var warnings = new List<string>();
        ReportConfig(config, errors, warnings);
        ReportPlugins(config, workingDir, warnings);
        ReportPluginScopes(config, workingDir, warnings);
        ReportPermissions(config, workingDir, warnings);

        Console.WriteLine();
        if (errors.Count == 0 && warnings.Count == 0)
        {
            WriteColor("No issues found.", ConsoleColor.Green);
            return 0;
        }

        if (errors.Count > 0)
        {
            WriteColor("Errors:", ConsoleColor.Red);
            foreach (var error in errors) WriteColor($"  ✗ {error}", ConsoleColor.Red);
        }
        if (warnings.Count > 0)
        {
            WriteColor("Warnings:", ConsoleColor.Yellow);
            foreach (var warning in warnings) WriteColor($"  ⚠ {warning}", ConsoleColor.Yellow);
        }
        return errors.Count > 0 ? 1 : 0;
    }

    private static void ReportConfig(CarbonConfig config, List<string> errors, List<string> warnings)
    {
        var targets = TargetPlatforms(config);
        Console.WriteLine($"Targets: {string.Join(", ", targets.Count > 0 ? targets : ["desktop"])}\n");

        foreach (var issue in ConfigValidator.Validate(config))
        {
            var text = $"{issue.Path} {issue.Message}";
            if (issue.Severity == ConfigSeverity.Error) errors.Add(text);
            else warnings.Add(text);
        }
    }

    /// <summary>Declared bundle targets as platform ids (desktop/android/ios), defaulting to desktop.</summary>
    private static IReadOnlyList<string> TargetPlatforms(CarbonConfig config)
    {
        var targets = config.Bundle.Targets
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(PluginCompatibility.PlatformForTarget)
            .Where(t => PluginCompatibility.Platforms.Contains(t))
            .Distinct()
            .ToList();
        return targets.Count > 0 ? targets : ["desktop"];
    }

    private static void ReportPlugins(CarbonConfig config, string workingDir, List<string> warnings)
    {
        var plugins = PluginCompatibility.Discover(workingDir);
        if (plugins.Count == 0)
        {
            Console.WriteLine("Plugins: none referenced.\n");
            return;
        }

        var targets = TargetPlatforms(config);
        Console.WriteLine($"Plugins ({plugins.Count} referenced):");
        foreach (var plugin in plugins)
        {
            Console.Write($"  {plugin.Namespace,-16} ");
            foreach (var platform in PluginCompatibility.Platforms)
            {
                var ok = plugin.Supports(platform);
                Console.Write($"{platform} ");
                WriteColor(ok ? "✓  " : "✗  ", ok ? ConsoleColor.Green : ConsoleColor.Red, newline: false);
            }
            Console.WriteLine();

            // Only warn about targets the app actually builds.
            var unsupported = targets.Where(p => !plugin.Supports(p)).ToList();
            if (unsupported.Count > 0)
                warnings.Add($"plugin '{plugin.Namespace}' is not compatible with: {string.Join(", ", unsupported)} " +
                             "(use `carbon bundle <platform> --allow-unsupported-plugins`).");
        }
        Console.WriteLine();
    }

    private static void ReportPluginScopes(CarbonConfig config, string workingDir, List<string> warnings)
    {
        var referenced = PluginCompatibility.Discover(workingDir)
            .Select(plugin => plugin.Namespace)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (referenced.Count == 0)
            return;

        if (referenced.Contains("fs"))
        {
            if (!TryGetPluginArray(config, "fs", "scopes", out var scopes) || scopes.Length == 0)
                warnings.Add("plugin 'fs' has no plugins.fs.scopes entries — file-system commands will be denied.");
        }

        if (referenced.Contains("http"))
        {
            if (!TryGetPluginArray(config, "http", "scope", out var scope) || scope.Length == 0)
                warnings.Add("plugin 'http' has no plugins.http.scope entries — any HTTP(S) URL is allowed in development mode.");
        }

        if (referenced.Contains("shell"))
        {
            if (!TryGetPluginArray(config, "shell", "allowedPrograms", out var allowedPrograms) || allowedPrograms.Length == 0)
                warnings.Add("plugin 'shell' has no plugins.shell.allowedPrograms entries — shell:execute will be denied.");

            if (TryGetPluginArray(config, "shell", "allowedEnv", out var allowedEnv) && allowedEnv.Contains("*", StringComparer.Ordinal))
                warnings.Add("plugin 'shell' uses allowedEnv = [\"*\"] — prefer explicit environment variable names.");
        }
    }

    private static void ReportPermissions(CarbonConfig config, string workingDir, List<string> warnings)
    {
        var enabled = PermissionCatalog.Enabled(config).ToList();
        if (enabled.Count == 0)
        {
            Console.WriteLine("Permissions: none requested.");
            return;
        }

        Console.WriteLine($"Permissions ({enabled.Count} requested):");
        foreach (var mapping in enabled)
        {
            var android = string.Join(", ", mapping.AndroidPermissions.Select(p => p.Replace("android.permission.", "")));
            var ios = mapping.IosUsageKey ?? "(runtime only)";
            Console.WriteLine($"  {mapping.Id,-14} android: {android,-28} ios: {ios}");

            if (mapping.IosUsageKey is not null &&
                !(config.Permissions.Descriptions.TryGetValue(mapping.Id, out var d) && !string.IsNullOrWhiteSpace(d)))
                warnings.Add($"permission '{mapping.Id}' has no custom iOS usage string — using the default " +
                             $"(set permissions.descriptions.{mapping.Id} before App Store submission).");
        }

        if (string.Equals(config.Permissions.Files, "external", StringComparison.OrdinalIgnoreCase))
            warnings.Add("permissions.files = \"external\" grants broad storage access — prefer \"appData\" or \"documents\" where possible.");

        var mobileAdded = PlatformService.KnownIds.Where(id => id is "android" or "ios")
            .Any(id => Directory.Exists(PlatformService.PlatformDir(workingDir, id)));
        if (!mobileAdded)
            warnings.Add("permissions are declared but no mobile platform is added (`carbon platform add android|ios`).");
    }

    private static bool TryGetPluginArray(CarbonConfig config, string plugin, string property, out string[] values)
    {
        values = [];
        if (!TryGetPluginConfig(config, plugin, out var configElement) ||
            !TryGetProperty(configElement, property, out var propertyElement))
            return false;

        if (propertyElement.ValueKind != JsonValueKind.Array)
            return false;

        values = propertyElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        return true;
    }

    private static bool TryGetPluginConfig(CarbonConfig config, string plugin, out JsonElement element)
    {
        foreach (var (key, value) in config.Plugins)
        {
            if (key.Equals(plugin, StringComparison.OrdinalIgnoreCase))
            {
                element = value;
                return true;
            }
        }

        element = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var item in element.EnumerateObject())
        {
            if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void WriteColor(string message, ConsoleColor color, bool newline = true)
    {
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(message);
        else Console.Write(message);
        Console.ResetColor();
    }
}
