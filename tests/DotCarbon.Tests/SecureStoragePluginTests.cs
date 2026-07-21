using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.SecureStorage;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.11: an encrypted key-value store on the OS credential store. The command logic is exercised
/// with a fake backend on every platform; the real macOS Keychain backend is verified only on macOS
/// (CI runs unit tests on Linux, where it isn't available).
/// </summary>
public class SecureStoragePluginTests
{
    [Fact]
    public void Set_get_remove_has_flow_through_the_store()
    {
        var store = new FakeStore();
        var plugin = new SecureStoragePlugin(store, "com.test.secure");

        Assert.False(plugin.Has(new KeyArgs("token")));
        Assert.Null(plugin.Get(new KeyArgs("token")));

        plugin.Set(new SecretArgs("token", "s3cret"));
        Assert.True(plugin.Has(new KeyArgs("token")));
        Assert.Equal("s3cret", plugin.Get(new KeyArgs("token")));
        // The service scopes the key.
        Assert.Equal(("com.test.secure", "token", "s3cret"), Assert.Single(store.Entries));

        plugin.Remove(new KeyArgs("token"));
        Assert.False(plugin.Has(new KeyArgs("token")));
        Assert.Null(plugin.Get(new KeyArgs("token")));
    }

    [Fact]
    public void Mac_keychain_round_trips_a_secret()
    {
        // The security CLI backend only exists on macOS; CI runs these on Linux.
        if (!OperatingSystem.IsMacOS()) return;

        var store = new MacKeychainStore();
        var service = "com.dotcarbon.test." + Guid.NewGuid().ToString("N");
        const string key = "api-key";
        const string value = "s3cret value !@#";
        try
        {
            Assert.False(store.Contains(service, key));

            store.Set(service, key, value);
            Assert.True(store.Contains(service, key));
            Assert.Equal(value, store.Get(service, key));

            store.Remove(service, key);
            Assert.False(store.Contains(service, key));
            Assert.Null(store.Get(service, key));
        }
        finally
        {
            store.Remove(service, key); // never leave test secrets in the real keychain
        }
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new SecureStoragePlugin(new FakeStore(), "svc").Register(registry);

        Assert.Contains("secure-storage:set", registry.Handlers.Keys);
        Assert.Contains("secure-storage:get", registry.Handlers.Keys);
        Assert.Contains("secure-storage:remove", registry.Handlers.Keys);
        Assert.Contains("secure-storage:has", registry.Handlers.Keys);
    }

    private sealed class FakeStore : ISecureStore
    {
        private readonly Dictionary<(string, string), string> _values = new();
        public List<(string service, string key, string value)> Entries =>
            _values.Select(pair => (pair.Key.Item1, pair.Key.Item2, pair.Value)).ToList();

        public void Set(string service, string key, string value) => _values[(service, key)] = value;
        public string? Get(string service, string key) => _values.GetValueOrDefault((service, key));
        public void Remove(string service, string key) => _values.Remove((service, key));
        public bool Contains(string service, string key) => _values.ContainsKey((service, key));
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
