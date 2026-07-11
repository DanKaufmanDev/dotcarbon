using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Platforms;

/// <summary>
/// Desktop needs no separate native shell — the <c>src-carbon</c> .NET host IS the desktop
/// shell (built by <c>carbon bundle desktop</c>). This generator just records that so
/// <c>carbon platform list</c> reports desktop alongside android/ios.
/// </summary>
internal sealed class DesktopPlatformGenerator : IPlatformGenerator
{
    public string Id => "desktop";
    public string DisplayName => "Desktop";

    public string ConfigSignature(CarbonConfig config) =>
        $"{config.App.Name}|{config.App.Identifier}|{config.App.Version}";

    public IReadOnlyList<GeneratedFile> Generate(PlatformContext context)
    {
        var name = context.Config.App.Name;
        return new List<GeneratedFile>
        {
            new("README.md",
                $"# {name} — Desktop\n\n" +
                "Desktop has no generated native shell: your `src-carbon` .NET host (via\n" +
                "`DotCarbon.Host.Desktop`) is the desktop application. Build and package it with:\n\n" +
                "```bash\n" +
                "carbon bundle desktop            # .app/.dmg, .msi, or .AppImage/.deb/.rpm\n" +
                "carbon bundle desktop --target osx-universal\n" +
                "```\n\n" +
                "This directory only exists so `carbon platform list` shows desktop next to the\n" +
                "mobile targets.\n"),
        };
    }
}
