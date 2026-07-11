using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Host;

/// <summary>
/// Callbacks the runtime wires into a webview at creation. The platform host connects
/// each one to its native event; the runtime stays platform-agnostic.
/// </summary>
public sealed class CarbonWebViewCallbacks
{
    /// <summary>A bridge message arrived from JavaScript.</summary>
    public Action<string>? MessageReceived { get; init; }

    /// <summary>The window is closing. Return <c>true</c> to cancel the close.</summary>
    public Func<bool>? Closing { get; init; }

    public Action? Creating { get; init; }
    public Action? Created { get; init; }
    public Action? Focused { get; init; }
    public Action? Blurred { get; init; }
    public Action<int, int>? Moved { get; init; }
    public Action<int, int>? Resized { get; init; }
    public Action? Minimized { get; init; }
    public Action? Maximized { get; init; }
    public Action? Restored { get; init; }
}

/// <summary>Everything the platform host needs to build one native webview.</summary>
public sealed class CarbonWebViewContext
{
    public required CarbonWindowOptions Options { get; init; }
    public ICarbonWebView? Parent { get; init; }
    public required CarbonWebViewCallbacks Callbacks { get; init; }
}
