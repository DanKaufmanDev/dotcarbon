using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.SecureStorage;

/// <summary>
/// An encrypted key-value store backed by the OS credential store (Task 6.11) — the macOS Keychain,
/// Windows DPAPI, or the Linux libsecret keyring. Secrets are scoped to the app's identifier. This is
/// DotCarbon's stronghold-equivalent: secrets never touch the frontend's storage.
/// </summary>
[CarbonPlugin("Secure Storage", description: "Encrypted key-value store on the OS credential store.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("secure-storage:default", "Allow all secure-storage commands.", Commands = new[] { "secure-storage:*" })]
public partial class SecureStoragePlugin : IPlugin
{
    private readonly ISecureStore _store;
    private readonly string _service;

    public SecureStoragePlugin(AppHandle app)
        : this(CreateStore(), app.Config.App.Identifier)
    {
    }

    internal SecureStoragePlugin(ISecureStore store, string service)
    {
        _store = store;
        _service = string.IsNullOrWhiteSpace(service) ? "dotcarbon.app" : service;
    }

    public string Namespace => "secure-storage";

    /// <summary>Store a secret.</summary>
    [CarbonCommand("set")]
    public void Set(SecretArgs args) => _store.Set(_service, args.Key, args.Value);

    /// <summary>Read a secret, or null if it isn't set.</summary>
    [CarbonCommand("get")]
    public string? Get(KeyArgs args) => _store.Get(_service, args.Key);

    /// <summary>Delete a secret.</summary>
    [CarbonCommand("remove")]
    public void Remove(KeyArgs args) => _store.Remove(_service, args.Key);

    /// <summary>Whether a secret is set.</summary>
    [CarbonCommand("has")]
    public bool Has(KeyArgs args) => _store.Contains(_service, args.Key);

    private static ISecureStore CreateStore()
    {
        if (OperatingSystem.IsMacOS()) return new MacKeychainStore();
        if (OperatingSystem.IsWindows()) return new WindowsDpapiStore();
        return new LinuxSecretToolStore();
    }
}
