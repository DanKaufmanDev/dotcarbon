using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Clipboard;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.4: Clipboard ported to the mobile native-binding pattern. The command surface routes to an
/// IClipboardProvider; the Android/iOS providers (DotCarbon.Plugins.Clipboard.Native) are verified by
/// building the mobile TFMs and on a device.
/// </summary>
public class ClipboardPluginTests
{
    [Fact]
    public async Task Read_write_clear_flow_through_the_provider()
    {
        var plugin = new ClipboardPlugin(new FakeClipboard());

        await plugin.WriteText(new WriteTextArgs("hello"));
        Assert.Equal("hello", await plugin.ReadText());

        await plugin.Clear();
        Assert.Equal(string.Empty, await plugin.ReadText());
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new ClipboardPlugin(new FakeClipboard()).Register(registry);

        Assert.Contains("clipboard:read_text", registry.Handlers.Keys);
        Assert.Contains("clipboard:write_text", registry.Handlers.Keys);
        Assert.Contains("clipboard:clear", registry.Handlers.Keys);
    }

    private sealed class FakeClipboard : IClipboardProvider
    {
        private string _text = string.Empty;
        public Task<string> ReadText() => Task.FromResult(_text);
        public Task WriteText(string text) { _text = text; return Task.CompletedTask; }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
