using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.WindowState;

/// <summary>
/// Persists each window's size, position, and maximized state and restores it on the next run
/// (Task 6.2). State is captured as windows move/resize/maximize and written on close/exit; it's
/// re-applied when a window is created (and to existing windows when the plugin initializes).
/// </summary>
[CarbonPlugin("Window State", description: "Persist and restore window size, position, and maximized state across runs.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("window-state:default", "Allow all window-state commands.", Commands = new[] { "window-state:*" })]
public partial class WindowStatePlugin : IPlugin
{
    private readonly AppHandle _app;
    private readonly object _gate = new();
    private Dictionary<string, WindowState> _states = new(StringComparer.Ordinal);
    private WindowStateOptions _options = new();

    public WindowStatePlugin(AppHandle app) => _app = app;

    public string Namespace => "window-state";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(WindowStateJsonContext.Default.WindowStateOptions);

        _states = Load();
        // The main window is created before plugins register, so restore it (and any others) now.
        foreach (var window in _app.Windows)
            Apply(window);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnLifecycleAsync(CarbonLifecycleEvent lifecycleEvent)
    {
        switch (lifecycleEvent.Kind)
        {
            case CarbonLifecycleEventKind.WindowCreated when lifecycleEvent.Window is { } created:
                Apply(created);
                break;

            case CarbonLifecycleEventKind.WindowMoved
                or CarbonLifecycleEventKind.WindowResized
                or CarbonLifecycleEventKind.WindowMaximized
                or CarbonLifecycleEventKind.WindowRestored when lifecycleEvent.Window is { } changed:
                Capture(changed);
                break;

            case CarbonLifecycleEventKind.WindowCloseRequested when lifecycleEvent.Window is { } closing:
                Capture(closing);
                Save();
                break;

            case CarbonLifecycleEventKind.Exiting:
                CaptureAllAndSave();
                break;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Capture every window's current geometry and write it now.</summary>
    [CarbonCommand("save")]
    public void SaveState() => CaptureAllAndSave();

    /// <summary>Re-apply the saved geometry to a window.</summary>
    [CarbonCommand("restore")]
    public void RestoreState(WindowLabelArgs args)
    {
        if (_app.TryGetWindow(args.Label, out var window))
            Apply(window);
    }

    /// <summary>The saved state for a window, or null if none.</summary>
    [CarbonCommand("get")]
    public WindowState? GetState(WindowLabelArgs args)
    {
        lock (_gate) return _states.GetValueOrDefault(args.Label);
    }

    /// <summary>Forget all saved state and delete the state file.</summary>
    [CarbonCommand("clear")]
    public void ClearState()
    {
        lock (_gate) _states.Clear();
        var path = StateFilePath();
        if (File.Exists(path)) File.Delete(path);
    }

    private void Capture(CarbonWindow window)
    {
        var state = new WindowState(
            window.Size.Width, window.Size.Height,
            window.Position.X, window.Position.Y,
            window.Native.IsMaximized);
        lock (_gate) _states[window.Label] = state;
    }

    private void Apply(CarbonWindow window)
    {
        WindowState? state;
        lock (_gate) state = _states.GetValueOrDefault(window.Label);
        if (state is null) return;

        if (state.Maximized)
        {
            window.Native.SetMaximized(true);
        }
        else
        {
            window.SetSize(state.Width, state.Height);
            window.SetPosition(state.X, state.Y);
        }
    }

    private void CaptureAllAndSave()
    {
        foreach (var window in _app.Windows)
            Capture(window);
        Save();
    }

    private Dictionary<string, WindowState> Load()
    {
        var path = StateFilePath();
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, WindowStateJsonContext.Default.DictionaryStringWindowState)
                ?? new Dictionary<string, WindowState>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, WindowState>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        var path = StateFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        Dictionary<string, WindowState> snapshot;
        lock (_gate) snapshot = new Dictionary<string, WindowState>(_states, StringComparer.Ordinal);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, WindowStateJsonContext.Default.DictionaryStringWindowState));
    }

    internal string StateFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.File))
            return Path.GetFullPath(_options.File);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configRoot = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support")
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(home, ".config");
        return Path.Combine(configRoot, _app.Config.App.Identifier, "window-state.json");
    }
}
