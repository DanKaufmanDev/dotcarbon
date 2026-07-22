namespace DotCarbon.Core.Host;

/// <summary>
/// Native modal dialogs, provided by the platform host — the host owns the window, so it owns modal UI
/// (the same reasoning as <see cref="ICarbonWebView"/>). The desktop host implements all of it via
/// Photino; the mobile hosts implement the alerts natively and report the file choosers as unsupported.
/// <c>DotCarbon.Plugins.Dialog</c> just exposes these as commands, so it needs no platform reference.
/// Arguments are primitives to keep the contract free of plugin types.
/// </summary>
public interface ICarbonDialogs
{
    /// <summary>Show an informational message with a single dismiss action.</summary>
    Task MessageAsync(string title, string message);

    /// <summary>Ask the user to confirm; true when they accept.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Pick one or more existing files. Null when the user cancels.</summary>
    Task<string[]?> OpenFileAsync(string title, string? defaultPath, bool multiple, string[]? filters);

    /// <summary>Pick a destination file. Null when the user cancels.</summary>
    Task<string?> SaveFileAsync(string title, string? defaultPath);

    /// <summary>Pick a directory. Null when the user cancels.</summary>
    Task<string?> OpenFolderAsync(string title, string? defaultPath);
}
