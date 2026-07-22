using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.DeepLink;

[CarbonPlugin("DeepLink", description: "Read app protocol URLs from process arguments (desktop) or platform intents (mobile).")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("deep-link:default", "Allow deep-link commands.", Commands = new[] { "deep-link:*" })]
[CarbonEvent("deep-link:opened", "string", "Raised for deep-link URLs at startup and while running.")]
public partial class DeepLinkPlugin : IPlugin
{
    private readonly object _gate = new();
    private string[] _schemes = [];
    private string[] _pending = [];

    public string Namespace => "deep-link";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _schemes = ResolveSchemes(context);

        var allowed = _schemes.Select(s => s.TrimEnd(':')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Matches(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && allowed.Contains(uri.Scheme);

        // Startup URLs: process arguments (desktop) and host-delivered launch URLs (mobile intents).
        _pending = Environment.GetCommandLineArgs().Where(Matches)
            .Concat(CarbonDeepLinks.Launch.Where(Matches))
            .Distinct()
            .ToArray();
        foreach (var url in _pending) Emit(context, url);

        // URLs delivered while running (Android onNewIntent / iOS openURL).
        CarbonDeepLinks.Subscribe(url =>
        {
            if (!Matches(url)) return;
            lock (_gate) _pending = _pending.Append(url).Distinct().ToArray();
            Emit(context, url);
        });
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("get_pending")]
    public string[] GetPending() { lock (_gate) return _pending; }

    [CarbonCommand("schemes")]
    public string[] Schemes() => _schemes;

    [CarbonCommand("info")]
    public DeepLinkInfo Info() { lock (_gate) return new(_schemes, _pending); }

    /// <summary>
    /// Schemes to accept: the plugin's own configuration if set, otherwise the app's declared
    /// <c>bundle.protocols</c> — the same source the desktop protocol registration and the mobile
    /// intent-filters / CFBundleURLTypes are generated from, so what the OS routes in is what the
    /// plugin accepts. Falls back to the identifier's last segment when nothing is declared.
    /// </summary>
    private static string[] ResolveSchemes(PluginContext context)
    {
        if (context.HasConfiguration)
        {
            var configured = context.GetConfiguration(DeepLinkJsonContext.Default.DeepLinkConfig).Schemes;
            if (configured.Length > 0) return configured;
        }

        var declared = context.App.Config.Bundle.Protocols
            .SelectMany(protocol => protocol.Schemes)
            .Select(scheme => scheme.Trim().TrimEnd(':'))
            .Where(scheme => scheme.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declared.Length > 0) return declared;

        return [context.App.Config.App.Identifier.Split('.').LastOrDefault() ?? context.App.Config.App.Name];
    }

    private static void Emit(PluginContext context, string url) =>
        _ = context.App.EmitAsync(
            new CarbonEventName<string>("deep-link:opened"), url, DeepLinkJsonContext.Default.String);
}
