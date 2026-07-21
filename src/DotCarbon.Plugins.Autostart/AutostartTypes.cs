namespace DotCarbon.Plugins.Autostart;

/// <summary>
/// Plugin configuration (<c>plugins.autostart</c>). <c>Args</c> are extra arguments passed to the app
/// when it launches at login; <c>EntryPath</c> overrides where the login entry is written (the
/// LaunchAgent plist on macOS, the .desktop file on Linux) — mainly for tests.
/// </summary>
public record AutostartOptions(string[]? Args = null, string? EntryPath = null);
