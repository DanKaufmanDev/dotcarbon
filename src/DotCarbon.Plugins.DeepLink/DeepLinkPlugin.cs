using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.DeepLink;

[CarbonPlugin("DeepLink", description: "Read app protocol URLs delivered through process arguments.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("deep-link:default", "Allow deep-link commands.", Commands = new[] { "deep-link:*" })]
[CarbonEvent("deep-link:opened", "string", "Raised for deep-link URLs discovered at startup.")]
public partial class DeepLinkPlugin : IPlugin
{
    private string[] _schemes = [];
    private string[] _pending = [];

    public string Namespace => "deep-link";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _schemes = context.HasConfiguration
            ? context.GetConfiguration<DeepLinkConfig>().Schemes
            : [context.App.Config.App.Identifier.Split('.').LastOrDefault() ?? context.App.Config.App.Name];
        _pending = FindUrls(_schemes);
        foreach (var url in _pending)
            _ = context.App.EmitAsync(new CarbonEventName<string>("deep-link:opened"), url);
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("get_pending")]
    public string[] GetPending() => _pending;

    [CarbonCommand("schemes")]
    public string[] Schemes() => _schemes;

    [CarbonCommand("info")]
    public DeepLinkInfo Info() => new(_schemes, _pending);

    private static string[] FindUrls(string[] schemes)
    {
        var allowed = schemes.Select(s => s.TrimEnd(':')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Environment.GetCommandLineArgs()
            .Where(arg => Uri.TryCreate(arg, UriKind.Absolute, out var uri) && allowed.Contains(uri.Scheme))
            .ToArray();
    }
}
