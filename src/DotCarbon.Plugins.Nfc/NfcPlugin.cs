using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Nfc;

/// <summary>
/// NDEF tag reading. Check <c>status</c> first — most devices in the wild have no NFC hardware, and
/// on Android the user may simply have it switched off, which is worth telling them.
/// </summary>
[CarbonPlugin("Nfc", description: "Read NFC (NDEF) tags.")]
[CarbonPluginPlatform("android", "ios")]
[CarbonPermission("nfc:default", "Allow reading NFC tags.", Commands = new[] { "nfc:*" })]
public partial class NfcPlugin : IPlugin
{
    internal const int MinTimeoutMs = 1_000;
    internal const int MaxTimeoutMs = 120_000;

    private readonly INfcProvider _provider;

    public NfcPlugin(AppHandle app)
        : this(app.Services.GetService<INfcProvider>() ?? new UnsupportedNfcProvider()) { }

    // Injection seam for tests and for the native binding.
    internal NfcPlugin(INfcProvider provider) => _provider = provider;

    public string Namespace => "nfc";

    [CarbonCommand("status")]
    public Task<string> Status() => _provider.StatusAsync();

    [CarbonCommand("scan")]
    public Task<NfcTag?> Scan(ScanArgs args) =>
        _provider.ScanAsync(args with { TimeoutMs = ClampTimeout(args.TimeoutMs) });

    internal static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, MinTimeoutMs, MaxTimeoutMs);
}
