using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.PersistedScope;

/// <summary>
/// Remembers scope roots granted at runtime across restarts (Task 6.9). When the app grants a folder to
/// the fs or asset scope — e.g. after the user picks it — <c>allow</c> both applies it (via
/// <see cref="CarbonRuntimeScope"/>) and saves it; on the next launch the saved roots are re-applied, so
/// the user doesn't have to grant access again. Mirrors Tauri's persisted-scope plugin.
/// </summary>
[CarbonPlugin("Persisted Scope", description: "Remember runtime-granted fs/asset scopes across restarts.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("persisted-scope:default", "Allow all persisted-scope commands.", Commands = new[] { "persisted-scope:*" })]
public partial class PersistedScopePlugin : IPlugin
{
    private readonly object _gate = new();
    private readonly AppHandle _app;
    private Dictionary<string, List<string>> _store = new(StringComparer.OrdinalIgnoreCase);
    private PersistedScopeOptions _options = new();

    public PersistedScopePlugin(AppHandle app) => _app = app;

    public string Namespace => "persisted-scope";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(PersistedScopeJsonContext.Default.PersistedScopeOptions);

        _store = Load();
        // Re-apply persisted roots so fs / convertFileSrc honor them again this run.
        foreach (var (scope, paths) in _store)
            foreach (var path in paths)
                CarbonRuntimeScope.Allow(scope, path);
        return ValueTask.CompletedTask;
    }

    /// <summary>Grant a path to a scope and persist it.</summary>
    [CarbonCommand("allow")]
    public void Allow(ScopeGrant grant)
    {
        CarbonRuntimeScope.Allow(grant.Scope, grant.Path);
        lock (_gate)
        {
            if (!_store.TryGetValue(grant.Scope, out var list)) _store[grant.Scope] = list = [];
            var full = Path.GetFullPath(grant.Path);
            if (!list.Contains(full, StringComparer.Ordinal)) list.Add(full);
        }
        Save();
    }

    /// <summary>The persisted grants, keyed by scope.</summary>
    [CarbonCommand("list")]
    public Dictionary<string, List<string>> List()
    {
        lock (_gate) return _store.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Forget all persisted grants and delete the store.</summary>
    [CarbonCommand("clear")]
    public void Clear()
    {
        lock (_gate) _store.Clear();
        CarbonRuntimeScope.Clear();
        var path = StoreFilePath();
        if (File.Exists(path)) File.Delete(path);
    }

    private Dictionary<string, List<string>> Load()
    {
        var path = StoreFilePath();
        if (!File.Exists(path)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, PersistedScopeJsonContext.Default.DictionaryStringListString)
                ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        var path = StoreFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Dictionary<string, List<string>> snapshot;
        lock (_gate) snapshot = _store.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value), StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, PersistedScopeJsonContext.Default.DictionaryStringListString));
    }

    internal string StoreFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.File))
            return Path.GetFullPath(_options.File);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataRoot = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support")
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(home, ".local", "share");
        return Path.Combine(dataRoot, _app.Config.App.Identifier, "persisted-scope.json");
    }
}
