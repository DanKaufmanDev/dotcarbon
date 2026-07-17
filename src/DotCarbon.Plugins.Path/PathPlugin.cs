using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using SysPath = System.IO.Path;

namespace DotCarbon.Plugins.Path;

/// <summary>
/// OS-standard app directories and path manipulation (Task 4.3). The app directories follow each
/// platform's own conventions (macOS <c>~/Library/…</c>, Windows <c>%APPDATA%</c>/<c>%LOCALAPPDATA%</c>,
/// Linux XDG) under the app's identifier from <c>carbon.json</c>. Path ops go through the backend so
/// they use the running OS's separator, which the frontend can't know on its own.
/// </summary>
[CarbonPlugin("Path", description: "OS-standard app directories and path manipulation.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("path:default", "Allow all path commands.", Commands = new[] { "path:*" })]
public partial class PathPlugin : IPlugin
{
    private readonly AppHandle _app;

    public PathPlugin(AppHandle app)
    {
        _app = app;
    }

    public string Namespace => "path";

    private string AppId => _app.Config.App.Identifier;

    // --- app directories ---------------------------------------------------------------------

    [CarbonCommand("home_dir")]
    public string HomeDir() => Home();

    [CarbonCommand("temp_dir")]
    public string TempDir() => Trim(SysPath.GetTempPath());

    /// <summary>Where bundled resources live — next to the running executable.</summary>
    [CarbonCommand("resource_dir")]
    public string ResourceDir() => Trim(AppContext.BaseDirectory);

    [CarbonCommand("app_config_dir")]
    public string AppConfigDir() => SysPath.Combine(ConfigRoot(), AppId);

    [CarbonCommand("app_data_dir")]
    public string AppDataDir() => SysPath.Combine(DataRoot(), AppId);

    [CarbonCommand("app_cache_dir")]
    public string AppCacheDir() => SysPath.Combine(CacheRoot(), AppId);

    [CarbonCommand("app_log_dir")]
    public string AppLogDir() => OperatingSystem.IsMacOS()
        ? SysPath.Combine(Home(), "Library", "Logs", AppId)
        : SysPath.Combine(DataRoot(), AppId, "logs");

    // --- path manipulation -------------------------------------------------------------------

    /// <summary>Combine and resolve to an absolute path.</summary>
    [CarbonCommand("resolve")]
    public string Resolve(PathPartsArgs args) => SysPath.GetFullPath(SysPath.Combine([.. args.Parts]));

    /// <summary>Join path segments with the OS separator (no resolution).</summary>
    [CarbonCommand("join")]
    public string Join(PathPartsArgs args) => SysPath.Combine([.. args.Parts]);

    [CarbonCommand("dirname")]
    public string Dirname(PathArg args) => SysPath.GetDirectoryName(args.Path) ?? string.Empty;

    [CarbonCommand("basename")]
    public string Basename(PathArg args) => SysPath.GetFileName(args.Path);

    [CarbonCommand("extname")]
    public string Extname(PathArg args) => SysPath.GetExtension(args.Path);

    [CarbonCommand("normalize")]
    public string Normalize(PathArg args) => SysPath.TrimEndingDirectorySeparator(SysPath.GetFullPath(args.Path));

    [CarbonCommand("is_absolute")]
    public bool IsAbsolute(PathArg args) => SysPath.IsPathFullyQualified(args.Path);

    [CarbonCommand("sep")]
    public string Sep() => SysPath.DirectorySeparatorChar.ToString();

    [CarbonCommand("delimiter")]
    public string Delimiter() => SysPath.PathSeparator.ToString();

    // --- per-OS roots ------------------------------------------------------------------------

    private static string Home() => Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static string ConfigRoot()
    {
        if (OperatingSystem.IsMacOS()) return SysPath.Combine(Home(), "Library", "Application Support");
        if (OperatingSystem.IsLinux()) return Xdg("XDG_CONFIG_HOME", ".config");
        // Windows and mobile: %APPDATA% / the platform's roaming-config equivalent.
        return Trim(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }

    private static string DataRoot()
    {
        if (OperatingSystem.IsMacOS()) return SysPath.Combine(Home(), "Library", "Application Support");
        if (OperatingSystem.IsLinux()) return Xdg("XDG_DATA_HOME", SysPath.Combine(".local", "share"));
        return Trim(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    private static string CacheRoot()
    {
        if (OperatingSystem.IsMacOS()) return SysPath.Combine(Home(), "Library", "Caches");
        if (OperatingSystem.IsLinux()) return Xdg("XDG_CACHE_HOME", ".cache");
        return Trim(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    private static string Xdg(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrEmpty(value) ? Trim(value) : SysPath.Combine(Home(), fallback);
    }

    private static string Trim(string path) => SysPath.TrimEndingDirectorySeparator(path);
}
