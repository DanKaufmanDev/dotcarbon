using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Nfc;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.5: NFC tag reading. The command surface routes to an INfcProvider; the Android
/// (NfcAdapter reader mode) and iOS (NFCNdefReaderSession) providers are verified by building the
/// mobile TFMs, and <c>nfc:status</c> on an emulator correctly reports no hardware. Actually
/// reading a tag needs physical hardware.
/// </summary>
public class NfcPluginTests
{
    [Fact]
    public async Task Status_and_scan_route_to_the_provider()
    {
        var tag = new NfcTag("04a2b3", [new NfcRecord("WellKnown", "T", "hello", [0x02, 0x65, 0x6e])]);
        var provider = new FakeNfc(NfcStatus.Available, tag);
        var plugin = new NfcPlugin(provider);

        Assert.Equal(NfcStatus.Available, await plugin.Status());

        var scanned = await plugin.Scan(new ScanArgs(5_000, "Tap to pay"));

        Assert.Equal("04a2b3", scanned!.Id);
        Assert.Equal("hello", Assert.Single(scanned.Records).Text);
        Assert.Equal((5_000, "Tap to pay"), (provider.LastArgs!.TimeoutMs, provider.LastArgs.Prompt));
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_resolves_to_null()
    {
        // Timeout and user-cancel are ordinary outcomes here, not errors.
        var plugin = new NfcPlugin(new FakeNfc(NfcStatus.Available, null));

        Assert.Null(await plugin.Scan(new ScanArgs()));
    }

    [Theory]
    [InlineData(0, NfcPlugin.MinTimeoutMs)]
    [InlineData(-5_000, NfcPlugin.MinTimeoutMs)]
    [InlineData(15_000, 15_000)]
    [InlineData(int.MaxValue, NfcPlugin.MaxTimeoutMs)]
    public async Task Timeouts_are_clamped_to_a_sane_range(int requested, int expected)
    {
        // A zero timeout would make scan useless, and an unbounded one leaves the reader session
        // (and on iOS, a system-modal sheet) open indefinitely.
        var provider = new FakeNfc(NfcStatus.Available, null);
        await new NfcPlugin(provider).Scan(new ScanArgs(requested));

        Assert.Equal(expected, provider.LastArgs!.TimeoutMs);
    }

    [Fact]
    public async Task Without_a_native_provider_it_reports_unsupported_and_scanning_throws()
    {
        // Desktop has no NFC, so status must say so plainly rather than pretend a scan is coming.
        var plugin = new NfcPlugin(new UnsupportedNfcProvider());

        Assert.Equal(NfcStatus.Unsupported, await plugin.Status());
        await Assert.ThrowsAsync<NotSupportedException>(() => plugin.Scan(new ScanArgs()));
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new NfcPlugin(new FakeNfc(NfcStatus.NoHardware, null)).Register(registry);

        Assert.Contains("nfc:status", registry.Handlers.Keys);
        Assert.Contains("nfc:scan", registry.Handlers.Keys);
    }

    private sealed class FakeNfc : INfcProvider
    {
        private readonly string _status;
        private readonly NfcTag? _tag;

        public FakeNfc(string status, NfcTag? tag)
        {
            _status = status;
            _tag = tag;
        }

        public ScanArgs? LastArgs { get; private set; }

        public Task<string> StatusAsync() => Task.FromResult(_status);

        public Task<NfcTag?> ScanAsync(ScanArgs args)
        {
            LastArgs = args;
            return Task.FromResult(_tag);
        }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
