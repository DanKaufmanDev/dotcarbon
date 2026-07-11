using System.Drawing;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using DotCarbon.Core.Host;

namespace DotCarbon.Core.Runtime;

public sealed class CarbonWindow
{
    private readonly CarbonApp _app;

    internal CarbonWindow(
        CarbonApp app,
        ICarbonPlatformHost platformHost,
        CarbonWindowOptions options,
        ICarbonWebView? parent)
    {
        _app = app;
        Options = options;
        Label = options.Label;

        var callbacks = new CarbonWebViewCallbacks
        {
            MessageReceived = message => _app.HandleMessage(this, message),
            Closing = () => _app.HandleWindowClosing(this),
            Creating = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowCreating, this),
            Created = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowCreated, this),
            Focused = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowFocused, this),
            Blurred = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowBlurred, this),
            Moved = (x, y) => _app.RaiseLifecycle(
                CarbonLifecycleEventKind.WindowMoved, this, new CarbonWindowPosition(x, y)),
            Resized = (width, height) => _app.RaiseLifecycle(
                CarbonLifecycleEventKind.WindowResized, this, new CarbonWindowSize(width, height)),
            Minimized = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowMinimized, this),
            Maximized = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowMaximized, this),
            Restored = () => _app.RaiseLifecycle(CarbonLifecycleEventKind.WindowRestored, this),
        };

        Native = platformHost.CreateWebView(new CarbonWebViewContext
        {
            Options = options,
            Parent = parent,
            Callbacks = callbacks,
        });
    }

    public string Label { get; }
    public AppHandle App => _app.Handle;
    public CarbonWindowOptions Options { get; }

    /// <summary>The platform webview backing this window (Photino on desktop).</summary>
    public ICarbonWebView Native { get; }

    public bool IsLoaded { get; private set; }
    public Uri? CurrentUri { get; private set; }

    public string Title => Native.Title;
    public Size Size => new(Native.Width, Native.Height);
    public Point Position => new(Native.X, Native.Y);

    public CarbonWindow SetTitle(string title)
    {
        Native.SetTitle(title);
        return this;
    }

    public CarbonWindow SetSize(int width, int height)
    {
        Native.SetSize(width, height);
        return this;
    }

    public CarbonWindow SetPosition(int x, int y)
    {
        Native.SetPosition(x, y);
        return this;
    }

    public CarbonWindow Load(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            CurrentUri = uri;
            Native.LoadUri(uri);
        }
        else
        {
            CurrentUri = null;
            Native.LoadUri(new Uri(url, UriKind.RelativeOrAbsolute));
        }
        IsLoaded = true;
        return this;
    }

    public CarbonWindow Load(Uri uri)
    {
        CurrentUri = uri;
        Native.LoadUri(uri);
        IsLoaded = true;
        return this;
    }

    public void Close() => Native.Close();

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
        return Native.SendMessageAsync(message);
    }

    internal void MarkLoaded() => IsLoaded = true;
}
