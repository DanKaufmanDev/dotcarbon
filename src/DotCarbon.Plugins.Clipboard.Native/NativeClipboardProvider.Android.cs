#if ANDROID
using Android.Content;
using Android.OS;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Clipboard;

namespace DotCarbon.Plugins.Clipboard.Native;

/// <summary>
/// Reads/writes the Android clipboard via <see cref="ClipboardManager"/>. Clipboard access must run on
/// the main looper, so both operations are marshalled there.
/// </summary>
internal sealed class NativeClipboardProvider : IClipboardProvider
{
    private readonly AppHandle _app;

    public NativeClipboardProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? global::Android.App.Application.Context;

    public Task<string> ReadText() => OnMain(() =>
    {
        var manager = Context.GetSystemService(Context.ClipboardService) as ClipboardManager;
        if (manager?.PrimaryClip is not { ItemCount: > 0 } clip) return string.Empty;
        return clip.GetItemAt(0)?.CoerceToText(Context)?.ToString() ?? string.Empty;
    });

    public Task WriteText(string text) => OnMain(() =>
    {
        var manager = Context.GetSystemService(Context.ClipboardService) as ClipboardManager;
        if (manager is not null) manager.PrimaryClip = ClipData.NewPlainText("text", text);
        return true;
    });

    private static Task<T> OnMain<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }
}
#endif
