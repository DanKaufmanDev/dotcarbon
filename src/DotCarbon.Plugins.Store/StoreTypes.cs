using System.Text.Json;

namespace DotCarbon.Plugins.Store;

public record StoreKeyArgs(string Key, string? Store = null);

public record StoreSetArgs(string Key, JsonElement Value, string? Store = null);

public record StoreLoadArgs(string? Store = null);

public record StoreEntry(string Key, JsonElement Value);

public record StoreSnapshot(string Store, StoreEntry[] Entries);
