namespace DotCarbon.Plugins.Clipboard;

public record WriteTextArgs(string Text);

/// <summary>
/// Reads/writes the platform clipboard. The desktop plugin ships a subprocess-based implementation;
/// a mobile app registers a native one (Android <c>ClipboardManager</c> / iOS <c>UIPasteboard</c>)
/// via <c>app.UseClipboard()</c> from the <c>DotCarbon.Plugins.Clipboard.Native</c> package.
/// </summary>
public interface IClipboardProvider
{
    Task<string> ReadText();
    Task WriteText(string text);
}