using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Security;

namespace DotCarbon.Core.Runtime;

public sealed class CarbonApp
{
    private readonly CarbonConfig _config;
    private readonly CommandRegistry _registry = new();
    private readonly CapabilityManager _capabilities;
    private readonly BridgeSecurityPolicy _bridgePolicy;
    private readonly AppHandleAccessor _handleAccessor = new();
    private readonly List<IPlugin> _plugins = [];
    private readonly List<IPlugin> _activePlugins = [];
    private readonly List<Func<IServiceProvider, IPlugin>> _pluginFactories = [];
    private readonly List<Func<AppHandle, CarbonWindow, IPlugin>> _windowPluginFactories = [];
    private readonly List<Action<AppHandle>> _setupHandlers = [];
    private readonly List<Action<CarbonLifecycleEvent>> _lifecycleHandlers = [];
    private IServiceProvider? _serviceProvider;
    private ICarbonPlatformHost? _platformHost;
    private AppHandle? _handle;
    private CarbonWindow? _mainWindow;
    private bool _ready;
    private bool _hasRun;
    private bool _shutdown;
    private int _exitLifecycleRaised;
    private ContentMode _contentMode;
    private string? _distIndexPath;
    private string? _distAssetRoot;

    private CarbonApp(CarbonConfig config)
    {
        _config = config;
        _capabilities = new CapabilityManager(config);
        _bridgePolicy = new BridgeSecurityPolicy(config);
        JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Services = new ServiceCollection();
        Services.AddSingleton(config);
        Services.AddSingleton(this);
        Services.AddSingleton(_capabilities);
        Services.AddSingleton(_bridgePolicy);
        Services.AddSingleton(_handleAccessor);
        Services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<AppHandleAccessor>().Handle);
        Services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<AppHandle>().Events);
    }

    public static CarbonApp Create(CarbonConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new CarbonApp(config);
    }

    public IServiceCollection Services { get; }
    public JsonSerializerOptions JsonOptions { get; }

    public AppHandle Handle => _handle
        ?? throw new InvalidOperationException(
            "The application handle is available during setup and after CarbonApp.Run begins.");

    public event EventHandler<CarbonLifecycleEvent>? Lifecycle;

    /// <summary>
    /// Supply the platform that creates windows and runs the message loop. Provided by a
    /// host package — e.g. <c>DotCarbon.Host.Desktop</c>'s <c>UseDesktop()</c> extension.
    /// </summary>
    public CarbonApp UsePlatform(ICarbonPlatformHost platformHost)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(platformHost);
        _platformHost = platformHost;
        return this;
    }

    /// <summary>The platform host's native handle (Android <c>Context</c> / iOS <c>WKWebView</c>), or null on desktop.</summary>
    internal object? PlatformNativeHandle => _platformHost?.NativeHandle;

    /// <summary>The platform host's native dialogs, or null when it provides none.</summary>
    internal ICarbonDialogs? PlatformDialogs => _platformHost?.Dialogs;

    public CarbonApp Manage<TState>(TState state) where TState : class
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(state);
        Services.AddSingleton(state);
        return this;
    }

    public CarbonApp ConfigureServices(Action<IServiceCollection> configure)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(configure);
        configure(Services);
        return this;
    }

    public CarbonApp ConfigureJson(Action<JsonSerializerOptions> configure)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(configure);
        configure(JsonOptions);
        return this;
    }

    public CarbonApp WithPlugin(IPlugin plugin)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(plugin);
        _plugins.Add(plugin);
        return this;
    }

    public CarbonApp WithPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPlugin>()
        where TPlugin : class, IPlugin
    {
        EnsureNotRunning();
        _pluginFactories.Add(services =>
            ActivatorUtilities.CreateInstance<TPlugin>(services));
        return this;
    }

    public CarbonApp UsePlugin(IPlugin plugin) => WithPlugin(plugin);

    public CarbonApp UsePlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPlugin>()
        where TPlugin : class, IPlugin =>
        WithPlugin<TPlugin>();

    public CarbonApp WithPluginFactory(Func<IServiceProvider, IPlugin> factory)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(factory);
        _pluginFactories.Add(factory);
        return this;
    }

    public CarbonApp WithWindowPlugin(
        Func<AppHandle, CarbonWindow, IPlugin> factory)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(factory);
        _windowPluginFactories.Add(factory);
        return this;
    }

    public CarbonApp Setup(Action<AppHandle> setup)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(setup);
        _setupHandlers.Add(setup);
        return this;
    }

    public CarbonApp OnLifecycle(Action<CarbonLifecycleEvent> handler)
    {
        EnsureNotRunning();
        ArgumentNullException.ThrowIfNull(handler);
        _lifecycleHandlers.Add(handler);
        return this;
    }

    // --- ergonomic lifecycle hooks (Task 4.5) ------------------------------------------------
    // Typed wrappers over OnLifecycle so apps don't switch on the event kind by hand.

    /// <summary>Run once the app is ready (services up, windows created, content loading).</summary>
    public CarbonApp OnReady(Action<AppHandle> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnLifecycle(e => { if (e.Kind == CarbonLifecycleEventKind.Ready) handler(e.App); });
    }

    /// <summary>
    /// Run when the app is asked to quit (the main window closing, for example). Call
    /// <see cref="CarbonExitRequest.Prevent"/> to keep it running.
    /// </summary>
    public CarbonApp OnExitRequested(Action<CarbonExitRequest> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnLifecycle(e =>
        {
            if (e.Kind != CarbonLifecycleEventKind.ExitRequested) return;
            var request = new CarbonExitRequest();
            handler(request);
            if (request.Prevented) e.Cancel = true;
        });
    }

    /// <summary>Run as the app is exiting, after any exit-requested prevention has passed.</summary>
    public CarbonApp OnBeforeExit(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnLifecycle(e => { if (e.Kind == CarbonLifecycleEventKind.Exiting) handler(); });
    }

    /// <summary>Run once the app's last window has closed.</summary>
    public CarbonApp OnWindowAllClosed(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnLifecycle(e => { if (e.Kind == CarbonLifecycleEventKind.WindowAllClosed) handler(); });
    }

    /// <summary>
    /// Build services, register commands, create windows, initialize plugins and load content —
    /// then return without blocking. A host that drives its own loop (desktop's message pump, or an
    /// Android <c>Activity</c> / iOS <c>UIApplication</c>) calls this, then owns the lifetime and
    /// calls <see cref="Shutdown"/> when the platform tears the app down. Desktop uses <see cref="Run"/>.
    /// </summary>
    public AppHandle Start()
    {
        EnsureNotRunning();
        if (_platformHost is null)
            throw new InvalidOperationException(
                "No platform host is configured. Add a host package (e.g. DotCarbon.Host.Desktop) and call " +
                "UseDesktop() before Run()/Start() (or supply an ICarbonPlatformHost via UsePlatform).");
        _hasRun = true;
        _serviceProvider = Services.BuildServiceProvider();
        _handle = new AppHandle(this, _config, _serviceProvider, JsonOptions);
        _handleAccessor.Handle = _handle;

        RegisterRuntimeCommands();
        PrepareContentMode();
        _capabilities.Configure(_contentMode == ContentMode.DevServer);
        _bridgePolicy.Configure(_contentMode == ContentMode.DevServer);
        EmbeddedAssetStore.Configure(_config.Security);
        Host.CarbonAssetScope.Configure(_config.Security.AssetScope);
        EmbeddedAssetStore.ConfigureLocalAssets(_contentMode == ContentMode.Dist ? _distAssetRoot : null);
        RaiseLifecycle(CarbonLifecycleEventKind.Starting);

        var mainOptions = CarbonWindowOptions.FromConfig(_config.Window);
        if (string.IsNullOrWhiteSpace(mainOptions.Label)) mainOptions.Label = "main";
        _config.Window.Label = mainOptions.Label;
        var mainWindow = _mainWindow = CreateWindow(mainOptions);

        foreach (var configuredWindow in _config.Windows)
            CreateWindow(CarbonWindowOptions.FromConfig(configuredWindow));

        _activePlugins.AddRange(_plugins);
        foreach (var factory in _pluginFactories)
            _activePlugins.Add(factory(_serviceProvider));
        foreach (var factory in _windowPluginFactories)
            _activePlugins.Add(factory(_handle, mainWindow));

        foreach (var plugin in _activePlugins)
        {
            InvokePluginInitialize(plugin);
            _registry.RegisterPlugin(plugin);
        }
        var pluginMetadata = _activePlugins.Select(plugin => plugin.Metadata).ToArray();
        _handle.SetPlugins(pluginMetadata);
        _capabilities.SetPluginMetadata(pluginMetadata);

        foreach (var setup in _setupHandlers) setup(_handle);
        foreach (var window in _handle.Windows.Where(window => !window.IsLoaded))
            LoadWindow(window);

        _ready = true;
        RaiseLifecycle(CarbonLifecycleEventKind.Ready);
        return _handle;
    }

    /// <summary>
    /// Desktop entry point: <see cref="Start"/>, block on the platform message loop until the main
    /// window closes, then tear down. Mobile hosts call <see cref="Start"/>/<see cref="Shutdown"/> instead.
    /// </summary>
    public void Run()
    {
        try
        {
            Start();
            _platformHost!.Run(_mainWindow!.Native);
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>
    /// Raise the exit lifecycle and dispose plugins and services. Called automatically by
    /// <see cref="Run"/>; mobile hosts call it when the platform destroys the app.
    /// </summary>
    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        try
        {
            RaiseExitLifecycle();
        }
        finally
        {
            try
            {
                DisposePlugins();
            }
            finally
            {
                (_serviceProvider as IDisposable)?.Dispose();
            }
        }
    }

    internal CarbonWindow CreateWindow(CarbonWindowOptions options)
    {
        if (_handle is null)
            throw new InvalidOperationException("Windows can only be created after CarbonApp.Run begins.");
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Label);

        CarbonWindow? parent = null;
        if (!string.IsNullOrWhiteSpace(options.ParentLabel))
            parent = _handle.GetWindow(options.ParentLabel);

        if (_handle.TryGetWindow(options.Label, out _))
            throw new InvalidOperationException(
                $"A Carbon window with label '{options.Label}' already exists.");

        var window = new CarbonWindow(this, _platformHost!, options, parent?.Native);
        _handle.AddWindow(window);
        if (_ready) LoadWindow(window);
        return window;
    }

    internal bool HandleWindowClosing(CarbonWindow window)
    {
        var closeRequested = RaiseLifecycle(
            CarbonLifecycleEventKind.WindowCloseRequested, window);
        if (closeRequested.Cancel) return true;

        if (ReferenceEquals(window, _mainWindow))
        {
            var exitRequested = RaiseLifecycle(
                CarbonLifecycleEventKind.ExitRequested, window);
            if (exitRequested.Cancel) return true;
        }

        var isMainWindow = ReferenceEquals(window, _mainWindow);
        _handle?.RemoveWindow(window);
        RaiseLifecycle(CarbonLifecycleEventKind.WindowClosed, window);

        // Task 4.5: once nothing is left open, tell the app before the exit lifecycle runs.
        if (_handle is { } handle && handle.Windows.Count == 0)
            RaiseLifecycle(CarbonLifecycleEventKind.WindowAllClosed, window);

        if (isMainWindow) RaiseExitLifecycle();
        return false;
    }

    internal void Exit() => _mainWindow?.Close();

    private void RaiseExitLifecycle()
    {
        if (Interlocked.Exchange(ref _exitLifecycleRaised, 1) != 0) return;
        RaiseLifecycle(CarbonLifecycleEventKind.Exiting, _mainWindow);
        RaiseLifecycle(CarbonLifecycleEventKind.Exited, _mainWindow);
    }

    internal CarbonLifecycleEvent RaiseLifecycle(
        CarbonLifecycleEventKind kind,
        CarbonWindow? window = null,
        object? data = null)
    {
        var lifecycleEvent = new CarbonLifecycleEvent(kind, Handle, window, data);
        foreach (var plugin in _activePlugins.ToArray())
            InvokePluginLifecycle(plugin, lifecycleEvent);
        foreach (var handler in _lifecycleHandlers.ToArray())
            InvokeLifecycleHandler(handler, lifecycleEvent);
        if (Lifecycle is not null)
        {
            foreach (EventHandler<CarbonLifecycleEvent> handler in Lifecycle.GetInvocationList())
            {
                try
                {
                    handler(this, lifecycleEvent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[Carbon] Lifecycle handler '{kind}' failed: {ex.Message}");
                }
            }
        }
        return lifecycleEvent;
    }

    private static void InvokeLifecycleHandler(
        Action<CarbonLifecycleEvent> handler,
        CarbonLifecycleEvent lifecycleEvent)
    {
        try
        {
            handler(lifecycleEvent);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[Carbon] Lifecycle handler '{lifecycleEvent.Kind}' failed: {ex.Message}");
        }
    }

    private void InvokePluginInitialize(IPlugin plugin)
    {
        try
        {
            var configuration = _config.Plugins.TryGetValue(plugin.Namespace, out var value)
                ? value
                : (JsonElement?)null;
            plugin.InitializeAsync(new PluginContext(Handle, configuration))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Plugin '{plugin.Namespace}' failed during initialization: {ex.Message}",
                ex);
        }
    }

    private static void InvokePluginLifecycle(
        IPlugin plugin,
        CarbonLifecycleEvent lifecycleEvent)
    {
        try
        {
            plugin.OnLifecycleAsync(lifecycleEvent).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[Carbon] Plugin '{plugin.Namespace}' lifecycle handler '{lifecycleEvent.Kind}' failed: {ex.Message}");
        }
    }

    private void DisposePlugins()
    {
        for (var i = _activePlugins.Count - 1; i >= 0; i--)
        {
            var plugin = _activePlugins[i];
            try
            {
                plugin.DisposeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[Carbon] Plugin '{plugin.Namespace}' teardown failed: {ex.Message}");
            }
        }
        _activePlugins.Clear();
    }

    internal async void HandleMessage(CarbonWindow window, string message)
    {
        string response;
        string? requestId = null;

        try
        {
            var bridgeMessage = JsonSerializer.Deserialize(
                message, CarbonCoreJsonContext.Default.BridgeMessage);
            if (bridgeMessage is null) return;
            requestId = bridgeMessage.Id;

            _bridgePolicy.EnsureBridgeMessageAllowed(window, message);
            _bridgePolicy.EnsureRequestIdAllowed(bridgeMessage.Id);
            _bridgePolicy.EnsureCommandNameAllowed(bridgeMessage.Command);
            _capabilities.EnsureCommandAllowed(window, bridgeMessage.Command);
            var data = await _registry.InvokeAsync(
                bridgeMessage.Command,
                bridgeMessage.Payload,
                new CarbonCommandContext(Handle, window));
            response = JsonSerializer.Serialize(
                new BridgeResponse("response", bridgeMessage.Id, true, data),
                CarbonCoreJsonContext.Default.BridgeResponse);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Bridge error: {ex.Message}");
            response = JsonSerializer.Serialize(
                new BridgeResponse(
                    "response", requestId ?? "error", false, JsonValue.Create(ex.Message)),
                CarbonCoreJsonContext.Default.BridgeResponse);
        }

        try
        {
            await window.Native.SendMessageAsync(response);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Could not send bridge response: {ex.Message}");
        }
    }

    private void RegisterRuntimeCommands()
    {
        _registry.Add("__carbon:event_emit", async payload =>
        {
            _bridgePolicy.EnsureEventEmitPayloadAllowed(payload);
            var eventName = payload.GetProperty("event").GetString();
            ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
            var eventPayload = payload.TryGetProperty("payload", out var value)
                ? JsonNode.Parse(value.GetRawText())
                : null;
            var target = ParseEventTarget(payload);
            if (target.Kind == CarbonEventTargetKind.Window &&
                !Handle.TryGetWindow(target.Label!, out _))
                throw new KeyNotFoundException(
                    $"No Carbon window is registered with label '{target.Label}'.");
            await Handle.Events.EmitJsonAsync(
                eventName,
                eventPayload,
                Handle.CurrentWindow.Label,
                target);
            return null;
        });
    }

    private static CarbonEventTarget ParseEventTarget(JsonElement payload)
    {
        if (!payload.TryGetProperty("target", out var target) ||
            target.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return CarbonEventTarget.All;

        if (target.ValueKind == JsonValueKind.String)
        {
            var value = target.GetString();
            return value switch
            {
                null or "all" => CarbonEventTarget.All,
                "app" => CarbonEventTarget.App,
                _ => CarbonEventTarget.Window(value),
            };
        }

        var kind = target.TryGetProperty("kind", out var kindValue)
            ? kindValue.GetString()
            : "all";
        return kind switch
        {
            "app" => CarbonEventTarget.App,
            "window" => CarbonEventTarget.Window(target.GetProperty("label").GetString()!),
            _ => CarbonEventTarget.All,
        };
    }

    private void PrepareContentMode()
    {
        if (EmbeddedAssetStore.HasAssets)
        {
            _contentMode = ContentMode.Embedded;
            Console.WriteLine("[Carbon] Production mode -> embedded frontend");
            return;
        }

        if (IsDevServerRunning(_config.Build.DevUrl))
        {
            _contentMode = ContentMode.DevServer;
            Console.WriteLine($"[Carbon] Development mode -> {_config.Build.DevUrl}");
            return;
        }

        _distIndexPath = ResolveFrontendDistIndex();
        if (_distIndexPath is not null)
        {
            _distAssetRoot = Path.GetDirectoryName(_distIndexPath);
            _contentMode = ContentMode.Dist;
            Console.WriteLine($"[Carbon] Local dist mode -> {_distIndexPath}");
            return;
        }

        _contentMode = ContentMode.Fallback;
        Console.WriteLine("[Carbon] No development server or frontend assets found");
    }

    private void LoadWindow(CarbonWindow window)
    {
        var path = window.Options.Url;
        if (!string.IsNullOrWhiteSpace(path) &&
            Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            _bridgePolicy.EnsureNavigationAllowed(absolute);
            window.Load(absolute);
            return;
        }

        path = string.IsNullOrWhiteSpace(path) ? "index.html" : path.TrimStart('/');
        switch (_contentMode)
        {
            case ContentMode.Embedded:
                var embeddedUri = new Uri("carbon://localhost/" + path);
                _bridgePolicy.EnsureNavigationAllowed(embeddedUri);
                window.Load(embeddedUri);
                break;
            case ContentMode.DevServer:
                var baseUrl = _config.Build.DevUrl.TrimEnd('/') + "/";
                var devUri = new Uri(new Uri(baseUrl), path == "index.html" ? string.Empty : path);
                _bridgePolicy.EnsureNavigationAllowed(devUri);
                window.Load(devUri);
                break;
            case ContentMode.Dist:
                var distUri = new Uri("carbon://localhost/" + path);
                _bridgePolicy.EnsureNavigationAllowed(distUri);
                window.Load(distUri);
                break;
            default:
                window.Native.LoadString(FallbackHtml());
                window.MarkLoaded();
                break;
        }
    }

    private static bool IsDevServerRunning(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            return client.GetAsync(url).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string? ResolveFrontendDistIndex()
    {
        var frontendDist = _config.Build.FrontendDist;
        if (string.IsNullOrWhiteSpace(frontendDist))
            return null;

        if (Path.IsPathRooted(frontendDist))
        {
            var index = Path.Combine(frontendDist, "index.html");
            return File.Exists(index) ? Path.GetFullPath(index) : null;
        }

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var index = Path.Combine(dir.FullName, frontendDist, "index.html");
            if (File.Exists(index))
                return Path.GetFullPath(index);

            dir = dir.Parent;
        }

        var appBaseIndex = Path.Combine(AppContext.BaseDirectory, frontendDist, "index.html");
        return File.Exists(appBaseIndex) ? Path.GetFullPath(appBaseIndex) : null;
    }

    private void EnsureNotRunning()
    {
        if (_hasRun)
            throw new InvalidOperationException("CarbonApp can only be configured before Run().");
    }

    private static string FallbackHtml() => """
        <html>
        <body style="font-family:system-ui;background:#1a1a1a;color:white;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
            <div>
                <h2>Carbon</h2>
                <p>No development server or frontend assets were found.</p>
            </div>
        </body>
        </html>
        """;

    private enum ContentMode
    {
        Embedded,
        DevServer,
        Dist,
        Fallback,
    }

    private sealed class AppHandleAccessor
    {
        public AppHandle Handle { get; set; } = null!;
    }
}
