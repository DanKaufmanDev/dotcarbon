#if IOS
using CoreFoundation;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Clipboard;
using UIKit;

namespace DotCarbon.Plugins.Clipboard.Native;

/// <summary>Reads/writes the iOS clipboard via <see cref="UIPasteboard"/> on the main queue.</summary>
internal sealed class NativeClipboardProvider : IClipboardProvider
{
    public NativeClipboardProvider(AppHandle app) { }

    public Task<string> ReadText()
    {
        var result = string.Empty;
        DispatchQueue.MainQueue.DispatchSync(() => result = UIPasteboard.General.String ?? string.Empty);
        return Task.FromResult(result);
    }

    public Task WriteText(string text)
    {
        DispatchQueue.MainQueue.DispatchSync(() => UIPasteboard.General.String = text);
        return Task.CompletedTask;
    }
}
#endif
