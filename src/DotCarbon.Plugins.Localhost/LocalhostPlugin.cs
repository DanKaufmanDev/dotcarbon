using System.Net;
using System.Net.Sockets;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Localhost;

/// <summary>
/// Serves the app's assets over a real <c>http://127.0.0.1:&lt;port&gt;</c> origin (Task 6.10), for webviews
/// or web APIs that need an http(s) origin rather than the <c>carbon://</c> scheme. Requests are answered
/// from the same asset pipeline as <c>carbon://</c> (embedded/dist assets and convertFileSrc files).
/// Mirrors Tauri's localhost plugin.
/// </summary>
[CarbonPlugin("Localhost", description: "Serve the app over a real http://127.0.0.1 port.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("localhost:default", "Allow all localhost commands.", Commands = new[] { "localhost:*" })]
public partial class LocalhostPlugin : IPlugin
{
    private readonly object _gate = new();
    private HttpListener? _listener;
    private string? _url;

    public string Namespace => "localhost";

    public ValueTask InitializeAsync(PluginContext context)
    {
        var options = context.HasConfiguration
            ? context.GetConfiguration(LocalhostJsonContext.Default.LocalhostOptions)
            : new LocalhostOptions();
        StartServer(options.Port);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        StopServer();
        return ValueTask.CompletedTask;
    }

    /// <summary>The URL the app is being served from, or "" if not running.</summary>
    [CarbonCommand("url")]
    public string Url()
    {
        lock (_gate) return _url ?? string.Empty;
    }

    /// <summary>(Re)start the server; returns the URL.</summary>
    [CarbonCommand("start")]
    public string Start(LocalhostStartArgs args)
    {
        StartServer(args.Port);
        return Url();
    }

    /// <summary>Stop the server.</summary>
    [CarbonCommand("stop")]
    public void Stop() => StopServer();

    private void StartServer(int port)
    {
        lock (_gate)
        {
            StopLocked();

            var resolved = port > 0 ? port : FreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{resolved}/");
            listener.Start();
            _listener = listener;
            _url = $"http://127.0.0.1:{resolved}";
            _ = ServeLoop(listener);
        }
    }

    private void StopServer()
    {
        lock (_gate) StopLocked();
    }

    private void StopLocked()
    {
        try { _listener?.Stop(); _listener?.Close(); }
        catch { /* already stopped */ }
        _listener = null;
        _url = null;
    }

    private static async Task ServeLoop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private static void Handle(HttpListenerContext context)
    {
        try
        {
            // Use the raw path so convertFileSrc's percent-encoded segment survives (Task 4.6).
            var raw = context.Request.RawUrl ?? "/";
            var carbonUrl = "carbon://localhost" + (raw is "/" or "" ? "/index.html" : raw);

            var asset = CarbonAssets.Serve(carbonUrl);
            context.Response.ContentType = asset.ContentType;
            using (asset.Content)
                asset.Content.CopyTo(context.Response.OutputStream);
        }
        catch
        {
            try { context.Response.StatusCode = 500; } catch { /* client gone */ }
        }
        finally
        {
            try { context.Response.Close(); } catch { /* client gone */ }
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
