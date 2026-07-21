using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCarbon.Plugins.SecureStorage;

/// <summary>A per-OS secret backend keyed by (service, key).</summary>
internal interface ISecureStore
{
    void Set(string service, string key, string value);
    string? Get(string service, string key);
    void Remove(string service, string key);
    bool Contains(string service, string key);
}

/// <summary>macOS Keychain via the <c>security</c> CLI (generic passwords).</summary>
internal sealed class MacKeychainStore : ISecureStore
{
    public void Set(string service, string key, string value) =>
        Run("add-generic-password", "-U", "-s", service, "-a", key, "-w", value);

    public string? Get(string service, string key)
    {
        var (exit, output) = Run("find-generic-password", "-s", service, "-a", key, "-w");
        return exit == 0 ? output.TrimEnd('\r', '\n') : null;
    }

    public void Remove(string service, string key) =>
        Run("delete-generic-password", "-s", service, "-a", key); // ignores "not found"

    public bool Contains(string service, string key) =>
        Run("find-generic-password", "-s", service, "-a", key).exit == 0;

    private static (int exit, string output) Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

/// <summary>Windows: values encrypted with DPAPI (current user) and kept in a per-service file.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiStore : ISecureStore
{
    public void Set(string service, string key, string value)
    {
        var store = Load(service);
        store[key] = Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser));
        Save(service, store);
    }

    public string? Get(string service, string key)
    {
        var store = Load(service);
        if (!store.TryGetValue(key, out var encrypted)) return null;
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Remove(string service, string key)
    {
        var store = Load(service);
        if (store.Remove(key)) Save(service, store);
    }

    public bool Contains(string service, string key) => Load(service).ContainsKey(key);

    private static string FilePath(string service) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), service, "secure-storage.json");

    private static Dictionary<string, string> Load(string service)
    {
        var path = FilePath(service);
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, SecureStorageJsonContext.Default.DictionaryStringString)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void Save(string service, Dictionary<string, string> store)
    {
        var path = FilePath(service);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(store, SecureStorageJsonContext.Default.DictionaryStringString));
    }
}

/// <summary>Linux: the libsecret keyring via the <c>secret-tool</c> CLI.</summary>
internal sealed class LinuxSecretToolStore : ISecureStore
{
    public void Set(string service, string key, string value) =>
        RunWithInput(value, "store", "--label", $"{service}/{key}", "service", service, "account", key);

    public string? Get(string service, string key)
    {
        var (exit, output) = Run("lookup", "service", service, "account", key);
        return exit == 0 ? output.TrimEnd('\r', '\n') : null;
    }

    public void Remove(string service, string key) =>
        Run("clear", "service", service, "account", key);

    public bool Contains(string service, string key) =>
        Run("lookup", "service", service, "account", key).exit == 0;

    private static (int exit, string output) Run(params string[] args) => Execute(input: null, args);

    private static void RunWithInput(string input, params string[] args) => Execute(input, args);

    private static (int exit, string output) Execute(string? input, string[] args)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
