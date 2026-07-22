using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Security;

namespace DotCarbon.Core.Runtime;

public sealed class AppHandle
{
    private readonly object _windowsGate = new();
    private readonly Dictionary<string, CarbonWindow> _windows =
        new(StringComparer.Ordinal);
    private readonly object _pluginsGate = new();
    private readonly List<PluginMetadata> _plugins = [];
    private readonly AsyncLocal<CarbonCommandContext?> _currentInvocation = new();
    private readonly CarbonApp _app;

    internal AppHandle(
        CarbonApp app,
        CarbonConfig config,
        IServiceProvider services,
        JsonSerializerOptions jsonOptions)
    {
        _app = app;
        Config = config;
        Services = services;
        JsonOptions = jsonOptions;
        Events = new CarbonEventBus(jsonOptions, RouteEventAsync);
    }

    public CarbonConfig Config { get; }
    public IServiceProvider Services { get; }
    public JsonSerializerOptions JsonOptions { get; }
    public CarbonEventBus Events { get; }

    /// <summary>
    /// The platform host's native handle for mobile plugin bindings — the Android <c>Context</c> or the
    /// iOS <c>WKWebView</c> — or null on desktop. A native binding casts it to the platform type it needs.
    /// </summary>
    public object? PlatformNativeHandle => _app.PlatformNativeHandle;

    /// <summary>
    /// The platform host's native dialogs (message/confirm/file choosers), or null when the host
    /// provides none. Used by <c>DotCarbon.Plugins.Dialog</c> so it needs no platform reference.
    /// </summary>
    public Host.ICarbonDialogs? PlatformDialogs => _app.PlatformDialogs;

    public IReadOnlyList<PluginMetadata> Plugins
    {
        get
        {
            lock (_pluginsGate) return _plugins.ToArray();
        }
    }

    public IReadOnlyList<CarbonWindow> Windows
    {
        get
        {
            lock (_windowsGate) return _windows.Values.ToArray();
        }
    }

    public CarbonWindow CurrentWindow =>
        _currentInvocation.Value?.Window ?? GetWindow(Config.Window.Label);

    public TState State<TState>() where TState : notnull =>
        Services.GetRequiredService<TState>();

    public CarbonWindow CreateWindow(CarbonWindowOptions options) =>
        _app.CreateWindow(options);

    public CarbonWindow CreateWindow(
        string label,
        Action<CarbonWindowOptions>? configure = null)
    {
        var options = new CarbonWindowOptions
        {
            Label = label,
            Title = Config.App.Name,
        };
        configure?.Invoke(options);
        return CreateWindow(options);
    }

    public CarbonWindow GetWindow(string label) =>
        TryGetWindow(label, out var window)
            ? window
            : throw new KeyNotFoundException($"No Carbon window is registered with label '{label}'.");

    public CarbonWindow GetWebview(string label) => GetWindow(label);

    public bool TryGetWindow(string label, out CarbonWindow window)
    {
        lock (_windowsGate) return _windows.TryGetValue(label, out window!);
    }

    public bool TryGetWebview(string label, out CarbonWindow webview) =>
        TryGetWindow(label, out webview);

    [RequiresUnreferencedCode("Use Events.EmitAsync with JsonTypeInfo for trimmed applications.")]
    [RequiresDynamicCode("Use Events.EmitAsync with JsonTypeInfo for NativeAOT applications.")]
    public Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        CarbonEventTarget? target = null) =>
        Events.EmitAsync(name, payload, target);

    public Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        JsonTypeInfo<T> typeInfo,
        CarbonEventTarget? target = null) =>
        Events.EmitAsync(name, payload, typeInfo, target);

    /// <summary>
    /// The merged <c>allow</c>/<c>deny</c> scopes granted to <paramref name="window"/> for a permission
    /// namespace (e.g. "fs"), unioned across every capability that applies to it. Plugins call this at
    /// invocation time — usually with <see cref="CurrentWindow"/> — to enforce per-capability scopes.
    /// Returns empty when security isn't configured.
    /// </summary>
    public CarbonPermissionScope ResolvePermissionScope(CarbonWindow window, string permissionNamespace)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionNamespace);
        return Services.GetService<CapabilityManager>()?.ResolveScope(window, permissionNamespace)
            ?? CarbonPermissionScope.Empty;
    }

    public void Exit() => _app.Exit();

    internal void AddWindow(CarbonWindow window)
    {
        lock (_windowsGate)
        {
            if (!_windows.TryAdd(window.Label, window))
                throw new InvalidOperationException(
                    $"A Carbon window with label '{window.Label}' already exists.");
        }
    }

    internal void SetPlugins(IEnumerable<PluginMetadata> plugins)
    {
        lock (_pluginsGate)
        {
            _plugins.Clear();
            _plugins.AddRange(plugins);
        }
    }

    internal void RemoveWindow(CarbonWindow window)
    {
        lock (_windowsGate)
        {
            if (_windows.GetValueOrDefault(window.Label) == window)
                _windows.Remove(window.Label);
        }
    }

    internal IDisposable EnterInvocation(CarbonCommandContext context)
    {
        var previous = _currentInvocation.Value;
        _currentInvocation.Value = context;
        // Mirror onto the static scope so a CarbonChannel deserialized from arguments can reach the
        // window (the converter has no AppHandle to consult). Task 4.1.
        Bridge.CarbonInvocationScope.Current = context;
        return new InvocationScope(_currentInvocation, previous);
    }

    private async Task RouteEventAsync(CarbonEventEnvelope envelope)
    {
        if (envelope.Target.Kind is CarbonEventTargetKind.All or CarbonEventTargetKind.App)
            await Events.DispatchAsync(envelope);

        if (envelope.Target.Kind == CarbonEventTargetKind.App) return;

        var windows = envelope.Target.Kind == CarbonEventTargetKind.Window
            ? [GetWindow(envelope.Target.Label!)]
            : Windows;
        await Task.WhenAll(windows.Select(window => window.SendEventAsync(envelope)));
    }

    private sealed class InvocationScope : IDisposable
    {
        private readonly AsyncLocal<CarbonCommandContext?> _slot;
        private readonly CarbonCommandContext? _previous;
        private bool _disposed;

        public InvocationScope(
            AsyncLocal<CarbonCommandContext?> slot,
            CarbonCommandContext? previous)
        {
            _slot = slot;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _slot.Value = _previous;
            Bridge.CarbonInvocationScope.Current = _previous;
            _disposed = true;
        }
    }
}
