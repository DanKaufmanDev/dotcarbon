using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.SingleInstance;

[CarbonPlugin("SingleInstance", description: "Detect and guard against duplicate app instances.")]
[CarbonPermission("single-instance:default", "Allow single-instance commands.", Commands = new[] { "single-instance:*" })]
public partial class SingleInstancePlugin : IPlugin
{
    private readonly CarbonConfig _config;
    private Mutex? _mutex;
    private bool _isPrimary;
    private string? _mutexName;

    public SingleInstancePlugin(CarbonConfig config)
    {
        _config = config;
    }

    public string Namespace => "single-instance";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _mutexName = "DotCarbon." + Sanitize(_config.App.Identifier);
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out _isPrimary);
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("status")]
    public SingleInstanceStatus Status() =>
        new(_isPrimary, _mutexName ?? string.Empty, Environment.GetCommandLineArgs());

    [CarbonCommand("is_primary")]
    public bool IsPrimary() => _isPrimary;

    public ValueTask DisposeAsync()
    {
        if (_isPrimary)
        {
            try { _mutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _mutex = null;
        return ValueTask.CompletedTask;
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
}
