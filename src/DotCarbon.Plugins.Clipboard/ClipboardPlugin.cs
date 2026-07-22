using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Clipboard;

[CarbonPlugin("Clipboard", description: "Read, write, and clear clipboard text.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("clipboard:default", "Allow all clipboard commands.", Commands = new[] { "clipboard:*" })]
public partial class ClipboardPlugin : IPlugin
{
    private readonly IClipboardProvider _provider;

    public ClipboardPlugin(AppHandle app)
        : this(app.Services.GetService<IClipboardProvider>() ?? new DesktopClipboardProvider()) { }

    // Injection seam for tests and for the native binding.
    internal ClipboardPlugin(IClipboardProvider provider) => _provider = provider;

    public string Namespace => "clipboard";

    [CarbonCommand("read_text")]
    public Task<string> ReadText() => _provider.ReadText();

    [CarbonCommand("write_text")]
    public Task WriteText(WriteTextArgs args) => _provider.WriteText(args.Text);

    [CarbonCommand("clear")]
    public Task Clear() => _provider.WriteText(string.Empty);
}
