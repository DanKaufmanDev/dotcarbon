using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Autostart;

/// <summary>
/// Launch the app at login (Task 6.3). Registers a per-user startup entry the way each OS expects:
/// a LaunchAgent plist on macOS, the <c>HKCU\…\Run</c> registry key on Windows, and a
/// <c>~/.config/autostart</c> .desktop file on Linux.
/// </summary>
[CarbonPlugin("Autostart", description: "Launch the app automatically at login.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("autostart:default", "Allow all autostart commands.", Commands = new[] { "autostart:*" })]
public partial class AutostartPlugin : IPlugin
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AppHandle _app;
    private AutostartOptions _options = new();

    public AutostartPlugin(AppHandle app) => _app = app;

    public string Namespace => "autostart";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(AutostartJsonContext.Default.AutostartOptions);
        return ValueTask.CompletedTask;
    }

    /// <summary>Register the app to launch at login.</summary>
    [CarbonCommand("enable")]
    public void Enable()
    {
        if (OperatingSystem.IsWindows())
        {
            RunReg($"add \"{RunKey}\" /v \"{AppId}\" /t REG_SZ /d \"{LaunchCommand()}\" /f");
            return;
        }

        var path = EntryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, OperatingSystem.IsMacOS() ? PlistContent() : DesktopEntryContent());
    }

    /// <summary>Stop launching the app at login.</summary>
    [CarbonCommand("disable")]
    public void Disable()
    {
        if (OperatingSystem.IsWindows())
        {
            RunReg($"delete \"{RunKey}\" /v \"{AppId}\" /f");
            return;
        }

        var path = EntryPath();
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Whether the app is currently set to launch at login.</summary>
    [CarbonCommand("is_enabled")]
    public bool IsEnabled() =>
        OperatingSystem.IsWindows()
            ? RunReg($"query \"{RunKey}\" /v \"{AppId}\"") == 0
            : File.Exists(EntryPath());

    private string AppId => _app.Config.App.Identifier;

    private string AppName =>
        string.IsNullOrWhiteSpace(_app.Config.App.Name) ? AppId : _app.Config.App.Name;

    private string Executable => Environment.ProcessPath ?? AppName;

    private string LaunchCommand()
    {
        var args = _options.Args ?? [];
        return args.Length == 0
            ? $"\"{Executable}\""
            : $"\"{Executable}\" {string.Join(' ', args)}";
    }

    internal string EntryPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.EntryPath))
            return Path.GetFullPath(_options.EntryPath);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "LaunchAgents", $"{AppId}.plist")
            : Path.Combine(home, ".config", "autostart", $"{AppId}.desktop");
    }

    private string PlistContent()
    {
        var arguments = string.Concat(
            new[] { Executable }.Concat(_options.Args ?? [])
                .Select(arg => $"\n    <string>{SecurityEscape(arg)}</string>"));
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            $"  <key>Label</key>\n  <string>{SecurityEscape(AppId)}</string>\n" +
            $"  <key>ProgramArguments</key>\n  <array>{arguments}\n  </array>\n" +
            "  <key>RunAtLoad</key>\n  <true/>\n" +
            "</dict>\n</plist>\n";
    }

    private string DesktopEntryContent() =>
        "[Desktop Entry]\n" +
        "Type=Application\n" +
        $"Name={AppName}\n" +
        $"Exec={LaunchCommand()}\n" +
        "X-GNOME-Autostart-enabled=true\n";

    private static string SecurityEscape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static int RunReg(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("reg", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null) return -1;
        process.WaitForExit();
        return process.ExitCode;
    }
}
