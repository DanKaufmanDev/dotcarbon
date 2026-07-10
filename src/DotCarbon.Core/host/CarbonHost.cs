using Photino.NET;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCarbon.Core.Config;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Core.Host;

public class CarbonHost
{
    private readonly CarbonConfig _config;
    private readonly CommandRegistry _registry = new();
    private readonly List<Func<PhotinoWindow, IPlugin>> _pluginFactories = new();
    private PhotinoWindow? _window;

    public PhotinoWindow Window => _window
        ?? throw new InvalidOperationException("Window not created yet. Access Window after Run() is called.");

    public CarbonHost(CarbonConfig config)
    {
        _config = config;
    }

    public CarbonHost WithPlugin(IPlugin plugin)
    {
        _registry.RegisterPlugin(plugin);
        return this;
    }

    public CarbonHost WithPlugin(Func<PhotinoWindow, IPlugin> factory)
    {
        _pluginFactories.Add(factory);
        return this;
    }

    public void Run()
    {
        var w = _config.Window;

        _window = new PhotinoWindow()
            .SetTitle(w.Title)
            .SetSize(w.Width, w.Height)
            .SetResizable(w.Resizable)
            .SetChromeless(!w.Decorations)
            .SetTransparent(w.Transparent)
            .SetTopMost(w.AlwaysOnTop)
            .SetMaximized(w.Maximized)
            .SetFullScreen(w.Fullscreen)
            .SetDevToolsEnabled(w.DevTools)
            .SetContextMenuEnabled(w.ContextMenu)
            .RegisterCustomSchemeHandler("carbon", EmbeddedAssetStore.Open)
            .RegisterWebMessageReceivedHandler(OnMessageReceived);

        if (w.MinWidth is int minW) _window.SetMinWidth(minW);
        if (w.MinHeight is int minH) _window.SetMinHeight(minH);
        if (w.MaxWidth is int maxW) _window.SetMaxWidth(maxW);
        if (w.MaxHeight is int maxH) _window.SetMaxHeight(maxH);

        if (w.Icon is { Length: > 0 } icon && File.Exists(icon))
            _window.SetIconFile(icon);

        if (w.X is int x && w.Y is int y)
            _window.SetLeft(x).SetTop(y);
        else if (w.Center)
            _window.Center();

        foreach (var factory in _pluginFactories)
            _registry.RegisterPlugin(factory(_window));

        var devUrl = _config.Build.DevUrl;
        if (EmbeddedAssetStore.HasAssets)
        {
            Console.WriteLine("[Carbon] Prod mode -> embedded frontend");
            _window.Load(new Uri("carbon://localhost/index.html"));
        }
        else if (IsDevServerRunning(devUrl))
        {
            Console.WriteLine($"[Carbon] Dev mode → {devUrl}");
            _window.Load(new Uri(devUrl));
        }
        else if (TryFindLooseFrontend(out var indexPath))
        {
            Console.WriteLine($"[Carbon] Prod mode -> {indexPath}");
            _window.Load(indexPath);
        }
        else
        {
            Console.WriteLine("[Carbon] No dev server or dist found, loading fallback");
            _window.LoadRawString(FallbackHtml());
        }

        _window.WaitForClose();
    }

    private bool TryFindLooseFrontend(out string indexPath)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, _config.Build.FrontendDist);
        var distPath = Directory.Exists(beside) ? beside : Path.GetFullPath(_config.Build.FrontendDist);
        indexPath = Path.Combine(distPath, "index.html");
        return File.Exists(indexPath);
    }

    private async void OnMessageReceived(object? sender, string message)
    {
        if (sender is not PhotinoWindow window) return;

        string response;
        string? requestId = null;

        try
        {
            var msg = JsonSerializer.Deserialize(message, CarbonCoreJsonContext.Default.BridgeMessage);
            if (msg is null) return;
            requestId = msg.Id;

            var data = await _registry.InvokeAsync(msg.Command, msg.Payload);

            response = JsonSerializer.Serialize(
                new BridgeResponse(msg.Id, true, data),
                CarbonCoreJsonContext.Default.BridgeResponse);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Carbon] Error: {ex.Message}");
            response = JsonSerializer.Serialize(
                new BridgeResponse(requestId ?? "error", false, JsonValue.Create(ex.Message)),
                CarbonCoreJsonContext.Default.BridgeResponse);
        }

        window.SendWebMessage(response);
    }

    private static bool IsDevServerRunning(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var result = client.GetAsync(url).GetAwaiter().GetResult();
            return result.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string FallbackHtml() => """
        <html>
        <body style="font-family:system-ui;background:#1a1a1a;color:white;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
            <div>
                <h2>⚡ Carbon</h2>
                <p>No dev server running and no dist/ folder found.</p>
            </div>
        </body>
        </html>
        """;
}

internal record BridgeMessage(string Id, string Command, JsonElement Payload);
internal record BridgeResponse(string Id, bool Ok, JsonNode? Data);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(JsonNode))]
internal partial class CarbonCoreJsonContext : JsonSerializerContext;
