using System.Drawing;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Photino.NET;
using DotCarbon.Core.Host;

namespace DotCarbon.Core.Runtime;

public sealed class CarbonWindow
{
    private readonly CarbonApp _app;

    internal CarbonWindow(
        CarbonApp app,
        CarbonWindowOptions options,
        CarbonWindow? parent)
    {
        _app = app;
        Options = options;
        Label = options.Label;

        NativeWindow = new PhotinoWindow(parent?.NativeWindow)
            .SetTitle(options.Title)
            .SetSize(options.Width, options.Height)
            .SetResizable(options.Resizable)
            .SetChromeless(!options.Decorations)
            .SetTransparent(options.Transparent)
            .SetTopMost(options.AlwaysOnTop)
            .SetMaximized(options.Maximized)
            .SetFullScreen(options.Fullscreen)
            .SetDevToolsEnabled(options.DevTools)
            .SetContextMenuEnabled(options.ContextMenu)
            .RegisterCustomSchemeHandler("carbon", EmbeddedAssetStore.Open)
            .RegisterWindowCreatingHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowCreating, this))
            .RegisterWindowCreatedHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowCreated, this))
            .RegisterWindowClosingHandler((_, _) => _app.HandleWindowClosing(this))
            .RegisterFocusInHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowFocused, this))
            .RegisterFocusOutHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowBlurred, this))
            .RegisterLocationChangedHandler((_, point) =>
                _app.RaiseLifecycle(
                    CarbonLifecycleEventKind.WindowMoved,
                    this,
                    new CarbonWindowPosition(point.X, point.Y)))
            .RegisterSizeChangedHandler((_, size) =>
                _app.RaiseLifecycle(
                    CarbonLifecycleEventKind.WindowResized,
                    this,
                    new CarbonWindowSize(size.Width, size.Height)))
            .RegisterMinimizedHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowMinimized, this))
            .RegisterMaximizedHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowMaximized, this))
            .RegisterRestoredHandler((_, _) =>
                _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowRestored, this))
            .RegisterWebMessageReceivedHandler((_, message) =>
                _app.HandleMessage(this, message));

        if (options.MinWidth is int minWidth) NativeWindow.SetMinWidth(minWidth);
        if (options.MinHeight is int minHeight) NativeWindow.SetMinHeight(minHeight);
        if (options.MaxWidth is int maxWidth) NativeWindow.SetMaxWidth(maxWidth);
        if (options.MaxHeight is int maxHeight) NativeWindow.SetMaxHeight(maxHeight);

        if (!string.IsNullOrWhiteSpace(options.Icon))
        {
            var icon = Path.GetFullPath(options.Icon);
            if (File.Exists(icon)) NativeWindow.SetIconFile(icon);
        }

        if (options.X is int x && options.Y is int y)
            NativeWindow.SetLeft(x).SetTop(y);
        else if (options.Center)
            NativeWindow.Center();
    }

    public string Label { get; }
    public AppHandle App => _app.Handle;
    public CarbonWindowOptions Options { get; }
    public PhotinoWindow NativeWindow { get; }
    public bool IsLoaded { get; private set; }
    public Uri? CurrentUri { get; private set; }

    public string Title => NativeWindow.Title;
    public Size Size => NativeWindow.Size;
    public Point Position => NativeWindow.Location;

    public CarbonWindow SetTitle(string title)
    {
        NativeWindow.SetTitle(title);
        return this;
    }

    public CarbonWindow SetSize(int width, int height)
    {
        NativeWindow.SetSize(width, height);
        return this;
    }

    public CarbonWindow SetPosition(int x, int y)
    {
        NativeWindow.SetLocation(new Point(x, y));
        return this;
    }

    public CarbonWindow Load(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            CurrentUri = uri;
            NativeWindow.Load(uri);
        }
        else
        {
            CurrentUri = null;
            NativeWindow.Load(url);
        }
        IsLoaded = true;
        return this;
    }

    public CarbonWindow Load(Uri uri)
    {
        CurrentUri = uri;
        NativeWindow.Load(uri);
        IsLoaded = true;
        return this;
    }

    public void Close() => NativeWindow.Close();

    [RequiresUnreferencedCode("Use App.Events.EmitAsync with JsonTypeInfo for trimmed applications.")]
    [RequiresDynamicCode("Use App.Events.EmitAsync with JsonTypeInfo for NativeAOT applications.")]
    public Task EmitAsync<T>(CarbonEventName<T> name, T payload) =>
        App.Events.EmitAsync(name, payload, Label, CarbonEventTarget.Window(Label));

    public Task EmitAsync<T>(
        CarbonEventName<T> name,
        T payload,
        JsonTypeInfo<T> typeInfo) =>
        App.Events.EmitAsync(
            name, payload, typeInfo, Label, CarbonEventTarget.Window(Label));

    internal Task SendEventAsync(CarbonEventEnvelope envelope)
    {
        var message = JsonSerializer.Serialize(
            new BridgeEventMessage(
                Type: "event",
                Id: envelope.Id,
                Event: envelope.Name,
                Payload: envelope.Payload,
                Source: envelope.SourceWindowLabel),
            CarbonCoreJsonContext.Default.BridgeEventMessage);
        return NativeWindow.SendWebMessageAsync(message);
    }

    internal void MarkLoaded() => IsLoaded = true;
}
