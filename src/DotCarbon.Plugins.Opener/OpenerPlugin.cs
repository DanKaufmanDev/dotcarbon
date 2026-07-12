using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Opener;

[CarbonPlugin("Opener", description: "Open files, folders, and URLs with the operating system.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("opener:default", "Allow opener commands.", Commands = new[] { "opener:*" })]
public partial class OpenerPlugin : IPlugin
{
    private static readonly HashSet<string> AllowedUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        Uri.UriSchemeFile,
    };

    public string Namespace => "opener";

    [CarbonCommand("open_path")]
    public bool OpenPath(OpenPathArgs args)
    {
        var path = Path.GetFullPath(args.Path);
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"Path does not exist: {path}");

        Open(path);
        return true;
    }

    [CarbonCommand("open_url")]
    public bool OpenUrl(OpenUrlArgs args)
    {
        if (!Uri.TryCreate(args.Url, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL must be absolute.");
        if (!AllowedUrlSchemes.Contains(uri.Scheme))
            throw new UnauthorizedAccessException($"URL scheme is not allowed: {uri.Scheme}");

        Open(uri.ToString());
        return true;
    }

    [CarbonCommand("reveal_path")]
    public bool RevealPath(OpenPathArgs args)
    {
        var path = Path.GetFullPath(args.Path);
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"Path does not exist: {path}");

        if (OperatingSystem.IsMacOS())
            Process.Start("open", new[] { "-R", path });
        else if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
        return true;
    }

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        });
    }
}
