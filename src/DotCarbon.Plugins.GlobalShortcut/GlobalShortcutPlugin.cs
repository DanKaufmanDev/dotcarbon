using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;

namespace DotCarbon.Plugins.GlobalShortcut;

[CarbonPlugin("GlobalShortcut", description: "Register global keyboard shortcuts.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("global-shortcut:default", "Allow global shortcut commands.", Commands = new[] { "global-shortcut:*" })]
[CarbonEvent("global-shortcut:pressed", nameof(GlobalShortcutPressed), "Raised when a registered shortcut is pressed.")]
public partial class GlobalShortcutPlugin : IPlugin
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ShortcutRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<KeyCode> _pressed = [];
    private SimpleGlobalHook? _hook;
    private AppHandle? _app;

    public string Namespace => "global-shortcut";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _app = context.App;
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("register")]
    public ShortcutInfo Register(RegisterShortcutArgs args)
    {
        var registration = Parse(args);
        lock (_gate)
        {
            _registrations[args.Id] = registration;
            EnsureHookLocked();
        }
        return registration.ToInfo();
    }

    [CarbonCommand("unregister")]
    public bool Unregister(ShortcutIdArgs args)
    {
        lock (_gate)
        {
            var removed = _registrations.Remove(args.Id);
            if (_registrations.Count == 0) StopHookLocked();
            return removed;
        }
    }

    [CarbonCommand("unregister_all")]
    public bool UnregisterAll()
    {
        lock (_gate)
        {
            _registrations.Clear();
            StopHookLocked();
            return true;
        }
    }

    [CarbonCommand("list")]
    public ShortcutInfo[] List()
    {
        lock (_gate)
            return _registrations.Values.Select(item => item.ToInfo()).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            StopHookLocked();
        return ValueTask.CompletedTask;
    }

    private void EnsureHookLocked()
    {
        if (_hook is not null) return;

        UioHookProvider.Instance.KeyTypedEnabled = false;
        _hook = new SimpleGlobalHook(GlobalHookType.Keyboard, null, runAsyncOnBackgroundThread: true);
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _ = _hook.RunAsync();
    }

    private void StopHookLocked()
    {
        if (_hook is null) return;

        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.Dispose();
        _hook = null;
        _pressed.Clear();
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated) return;

        ShortcutRegistration? match = null;
        lock (_gate)
        {
            _pressed.Add(e.Data.KeyCode);
            match = _registrations.Values.FirstOrDefault(registration => registration.Matches(_pressed));
        }

        if (match is null) return;
        if (match.Suppress) e.SuppressEvent = true;

        if (_app is not null)
            _ = _app.EmitAsync(
                new CarbonEventName<GlobalShortcutPressed>("global-shortcut:pressed"),
                new GlobalShortcutPressed(match.Id, match.Accelerator));
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_gate)
            _pressed.Remove(e.Data.KeyCode);
    }

    private static ShortcutRegistration Parse(RegisterShortcutArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
            throw new ArgumentException("Shortcut id is required.");

        var parts = args.Accelerator
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Accelerator is required.");

        var keys = parts.Select(ParseKey).ToHashSet();
        return new ShortcutRegistration(args.Id, args.Accelerator, keys, args.Suppress);
    }

    private static KeyCode ParseKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant() switch
        {
            "cmd" or "command" or "meta" or "super" => OperatingSystem.IsMacOS()
                ? nameof(KeyCode.VcLeftMeta)
                : nameof(KeyCode.VcLeftControl),
            "cmdorctrl" or "commandorcontrol" or "mod" => OperatingSystem.IsMacOS()
                ? nameof(KeyCode.VcLeftMeta)
                : nameof(KeyCode.VcLeftControl),
            "ctrl" or "control" => nameof(KeyCode.VcLeftControl),
            "shift" => nameof(KeyCode.VcLeftShift),
            "alt" or "option" => nameof(KeyCode.VcLeftAlt),
            "enter" or "return" => nameof(KeyCode.VcEnter),
            "space" => nameof(KeyCode.VcSpace),
            "escape" or "esc" => nameof(KeyCode.VcEscape),
            var key when key.StartsWith('f') && int.TryParse(key[1..], out var number) => $"VcF{number}",
            var key when key.Length == 1 && char.IsLetterOrDigit(key[0]) => "Vc" + char.ToUpperInvariant(key[0]),
            var key when key.StartsWith("vc") => "Vc" + key[2..],
            var key => key,
        };

        if (!Enum.TryParse<KeyCode>(normalized, ignoreCase: true, out var parsed))
            throw new ArgumentException($"Unsupported shortcut key: {value}");
        return parsed;
    }

    private sealed record ShortcutRegistration(
        string Id,
        string Accelerator,
        HashSet<KeyCode> Keys,
        bool Suppress)
    {
        public bool Matches(HashSet<KeyCode> pressed) => Keys.All(pressed.Contains);
        public ShortcutInfo ToInfo() => new(Id, Accelerator, Suppress);
    }
}
