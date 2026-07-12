using System.Runtime.InteropServices;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Os;

[CarbonPlugin("Os", description: "Read operating system information (platform, arch, version, …).")]
[CarbonPermission("os:default", "Allow all OS commands.", Commands = new[] { "os:*" })]
public partial class OsPlugin : IPlugin
{
    public string Namespace => "os";

    [CarbonCommand("info")]
    public OsInfo Info() =>
        new(Platform(), Arch(), Version(), Family(), Hostname(), ExeExtension(), Environment.NewLine);

    [CarbonCommand("platform")]
    public string Platform() =>
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsAndroid() ? "android" :
        OperatingSystem.IsIOS() ? "ios" : "unknown";

    [CarbonCommand("arch")]
    public string Arch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        Architecture.Arm64 => "aarch64",
        var other => other.ToString().ToLowerInvariant(),
    };

    [CarbonCommand("version")]
    public string Version() => Environment.OSVersion.Version.ToString();

    [CarbonCommand("family")]
    public string Family() => OperatingSystem.IsWindows() ? "windows" : "unix";

    [CarbonCommand("hostname")]
    public string Hostname() => Environment.MachineName;

    private static string ExeExtension() => OperatingSystem.IsWindows() ? ".exe" : string.Empty;
}
