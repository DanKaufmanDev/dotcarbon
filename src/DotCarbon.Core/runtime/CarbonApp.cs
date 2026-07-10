using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Core.Runtime;

public sealed class CarbonApp
{
    private readonly CarbonConfig _config;
    private readonly CommandRegistry _registry = new();
    private readonly AppHandleAccessor _handleAccessor = new();
    private readonly List<IPlugin> _plugins = [];
    private readonly List<Func<IServiceProvider, IPlugin>> _pluginFactories = [];
    private readonly List<Func<AppHandle, CarbonWindow, IPlugin>> _windowPluginFactories = [];
    private readonly List<Action<AppHandle>> _setupHandlers = [];
    private readonly List<Action<CarbonLifecycleEvent>> _lifecycleHandlers = [];
    private IServiceProvider? _serviceProvider;
    private AppHandle? _handle;
    private CarbonWindow? _mainWindow;
    private bool _ready;
    private bool _hasRun;
    private int _exitLifecycleRaised;
    private ContentMode _contentMode;
    private string? _looseFrontendDirectory;

    private CarbonApp(CarbonConfig config)
    {
        _config = config;
        JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Services = new ServiceCollection();
        Services.AddSingleton(config);
        Services.AddSingleton(this);
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

    public void Run()
    {
        EnsureNotRunning();
        _hasRun = true;
        _serviceProvider = Services.BuildServiceProvider();
        _handle = new AppHandle(this, _config, _serviceProvider, JsonOptions);
        _handleAccessor.Handle = _handle;

        try
        {
            RegisterRuntimeCommands();
            PrepareContentMode();
            RaiseLifecycle(CarbonLifecycleEventKind.Starting);

            var mainOptions = CarbonWindowOptions.FromConfig(_config.Window);
            if (string.IsNullOrWhiteSpace(mainOptions.Label)) mainOptions.Label = "main";
            _config.Window.Label = mainOptions.Label;
            var mainWindow = _mainWindow = CreateWindow(mainOptions);

            foreach (var configuredWindow in _config.Windows)
                CreateWindow(CarbonWindowOptions.FromConfig(configuredWindow));

            foreach (var plugin in _plugins) _registry.RegisterPlugin(plugin);
            foreach (var factory in _pluginFactories)
                _registry.RegisterPlugin(factory(_serviceProvider));
            foreach (var factory in _windowPluginFactories)
                _registry.RegisterPlugin(factory(_handle, mainWindow));

            foreach (var setup in _setupHandlers) setup(_handle);
            foreach (var window in _handle.Windows.Where(window => !window.IsLoaded))
                LoadWindow(window);

            _ready = true;
            RaiseLifecycle(CarbonLifecycleEventKind.Ready);
            mainWindow.NativeWindow.WaitForClose();
        }
        finally
        {
            try
            {
                RaiseExitLifecycle();
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

        var window = new CarbonWindow(this, options, parent);
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
        if (!isMainWindow) _handle?.RemoveWindow(window);
        RaiseLifecycle(CarbonLifecycleEventKind.WindowClosed, window);
        if (isMainWindow)
        {
            RaiseExitLifecycle();
            _handle?.RemoveWindow(window);
        }
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
            await window.NativeWindow.SendWebMessageAsync(response);
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
            var eventName = payload.GetProperty("event").GetString();
            ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
            var eventPayload = payload.TryGetProperty("payload", out var value)
                ? JsonNode.Parse(value.GetRawText())
                : null;
            var target = ParseEventTarget(payload);
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

        if (TryFindLooseFrontend(out var directory))
        {
            _contentMode = ContentMode.Loose;
            _looseFrontendDirectory = directory;
            Console.WriteLine($"[Carbon] Production mode -> {directory}");
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
            window.Load(absolute);
            return;
        }

        path = string.IsNullOrWhiteSpace(path) ? "index.html" : path.TrimStart('/');
        switch (_contentMode)
        {
            case ContentMode.Embedded:
                window.Load(new Uri("carbon://localhost/" + path));
                break;
            case ContentMode.DevServer:
                var baseUrl = _config.Build.DevUrl.TrimEnd('/') + "/";
                window.Load(new Uri(new Uri(baseUrl), path == "index.html" ? string.Empty : path));
                break;
            case ContentMode.Loose:
                window.Load(Path.Combine(_looseFrontendDirectory!, path));
                break;
            default:
                window.NativeWindow.LoadRawString(FallbackHtml());
                window.MarkLoaded();
                break;
        }
    }

    private bool TryFindLooseFrontend(out string directory)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, _config.Build.FrontendDist);
        directory = Directory.Exists(beside)
            ? Path.GetFullPath(beside)
            : Path.GetFullPath(_config.Build.FrontendDist);
        return File.Exists(Path.Combine(directory, "index.html"));
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
        Loose,
        Fallback,
    }

    private sealed class AppHandleAccessor
    {
        public AppHandle Handle { get; set; } = null!;
    }
}
