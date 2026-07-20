namespace DotCarbon.Plugins.WindowState;

/// <summary>A window's persisted geometry.</summary>
public record WindowState(int Width, int Height, int X, int Y, bool Maximized);

public record WindowLabelArgs(string Label);

/// <summary>Plugin configuration (<c>plugins.window-state</c>). <c>File</c> overrides the state file path.</summary>
public record WindowStateOptions(string? File = null);
