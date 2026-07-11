using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Store;

[CarbonPlugin("Store", description: "Persistent JSON key/value stores.")]
[CarbonPermission("store:default", "Allow persistent store commands.", Commands = new[] { "store:*" })]
public partial class StorePlugin : IPlugin
{
    private readonly CarbonConfig _config;
    private readonly object _gate = new();

    public StorePlugin(CarbonConfig config)
    {
        _config = config;
    }

    public string Namespace => "store";

    [CarbonCommand("get")]
    public JsonElement? Get(StoreKeyArgs args)
    {
        lock (_gate)
        {
            var document = Load(args.Store);
            return document.TryGetProperty(args.Key, out var value)
                ? Clone(value)
                : null;
        }
    }

    [CarbonCommand("set")]
    public StoreSnapshot Set(StoreSetArgs args)
    {
        lock (_gate)
        {
            var store = StoreName(args.Store);
            var node = LoadNode(store);
            node[args.Key] = JsonNode.Parse(args.Value.GetRawText());
            SaveNode(store, node);
            return Snapshot(store, node);
        }
    }

    [CarbonCommand("delete")]
    public StoreSnapshot Delete(StoreKeyArgs args)
    {
        lock (_gate)
        {
            var store = StoreName(args.Store);
            var node = LoadNode(store);
            node.Remove(args.Key);
            SaveNode(store, node);
            return Snapshot(store, node);
        }
    }

    [CarbonCommand("clear")]
    public StoreSnapshot Clear(StoreLoadArgs args)
    {
        lock (_gate)
        {
            var store = StoreName(args.Store);
            var node = new JsonObject();
            SaveNode(store, node);
            return Snapshot(store, node);
        }
    }

    [CarbonCommand("entries")]
    public StoreSnapshot Entries(StoreLoadArgs args)
    {
        lock (_gate)
        {
            var store = StoreName(args.Store);
            return Snapshot(store, LoadNode(store));
        }
    }

    [CarbonCommand("keys")]
    public string[] Keys(StoreLoadArgs args)
    {
        lock (_gate)
            return LoadNode(StoreName(args.Store)).Select(entry => entry.Key).Order().ToArray();
    }

    private JsonElement Load(string? store)
    {
        using var document = JsonDocument.Parse(LoadNode(StoreName(store)).ToJsonString());
        return Clone(document.RootElement);
    }

    private JsonObject LoadNode(string store)
    {
        var path = StorePath(store);
        if (!File.Exists(path)) return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private void SaveNode(string store, JsonObject node)
    {
        var path = StorePath(store);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private StoreSnapshot Snapshot(string store, JsonObject node)
    {
        var entries = node
            .Select(entry =>
            {
                using var document = JsonDocument.Parse((entry.Value ?? JsonValue.Create((string?)null)!).ToJsonString());
                return new StoreEntry(entry.Key, Clone(document.RootElement));
            })
            .ToArray();
        return new StoreSnapshot(store, entries);
    }

    private string StorePath(string store) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DotCarbon",
            Sanitize(_config.App.Identifier),
            "stores",
            Sanitize(store) + ".json");

    private static string StoreName(string? store) =>
        string.IsNullOrWhiteSpace(store) ? "default" : store.Trim();

    private static string Sanitize(string value) =>
        string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));

    private static JsonElement Clone(JsonElement element)
    {
        using var document = JsonDocument.Parse(element.GetRawText());
        return document.RootElement.Clone();
    }
}
