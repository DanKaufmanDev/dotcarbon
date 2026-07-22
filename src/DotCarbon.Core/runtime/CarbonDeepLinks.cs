namespace DotCarbon.Core.Runtime;

/// <summary>
/// Process-global sink for deep-link URLs delivered by a platform host — Android intents
/// (<c>onCreate</c>/<c>onNewIntent</c>) and iOS <c>openURL</c>. On desktop the URL arrives as a process
/// argument and the DeepLink plugin reads it directly; on mobile the host records it here instead.
/// The plugin reads <see cref="Launch"/> at startup and subscribes for URLs that arrive while running.
/// </summary>
public static class CarbonDeepLinks
{
    private static readonly object Gate = new();
    private static readonly List<string> LaunchUrls = [];
    private static Action<string>? _handler;

    /// <summary>
    /// Records a deep-link URL from the platform host. Before the plugin subscribes (i.e. at launch)
    /// the URL is just queued into <see cref="Launch"/>; afterwards it is also delivered live.
    /// </summary>
    public static void Deliver(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Action<string>? handler;
        lock (Gate)
        {
            LaunchUrls.Add(url);
            handler = _handler;
        }
        handler?.Invoke(url);
    }

    /// <summary>URLs the app has been given so far (launch URLs plus any delivered while running).</summary>
    public static IReadOnlyList<string> Launch
    {
        get { lock (Gate) return LaunchUrls.ToArray(); }
    }

    /// <summary>Subscribe to deep-link URLs that arrive after startup. Only one subscriber is kept.</summary>
    public static void Subscribe(Action<string> handler)
    {
        lock (Gate) _handler = handler;
    }

    // Test hook: reset the process-global state between cases.
    internal static void Reset()
    {
        lock (Gate)
        {
            LaunchUrls.Clear();
            _handler = null;
        }
    }
}
