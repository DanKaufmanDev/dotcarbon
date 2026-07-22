using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Plugins.Dialog;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.4: the Dialog plugin no longer owns platform code — native dialogs come from the host via
/// ICarbonDialogs, which is what lets the same plugin run on desktop and mobile. These pin the
/// delegation; the per-host implementations are verified by building and on a device.
/// </summary>
public class DialogPluginTests
{
    [Fact]
    public async Task Commands_delegate_to_the_host_dialogs()
    {
        var dialogs = new FakeDialogs();
        var plugin = new DialogPlugin(dialogs);

        await plugin.Message(new MessageArgs("Title", "Body"));
        Assert.Equal(("Title", "Body"), dialogs.LastMessage);

        Assert.True(await plugin.Confirm(new ConfirmArgs("Confirm", "Sure?")));
        Assert.Equal(("Confirm", "Sure?"), dialogs.LastConfirm);

        var opened = await plugin.OpenFile(new OpenFileArgs("Open", "/start", true, [".txt"]));
        Assert.NotNull(opened);
        Assert.Equal(["/picked.txt"], opened);
        Assert.Equal(("Open", "/start", true), dialogs.LastOpen);

        Assert.Equal("/saved.txt", await plugin.SaveFile(new SaveFileArgs("Save", DefaultName: "saved.txt")));
        Assert.Equal("/folder", await plugin.OpenFolder(new OpenFolderArgs("Pick")));
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new DialogPlugin(new FakeDialogs()).Register(registry);

        Assert.Contains("dialog:message", registry.Handlers.Keys);
        Assert.Contains("dialog:confirm", registry.Handlers.Keys);
        Assert.Contains("dialog:open_file", registry.Handlers.Keys);
        Assert.Contains("dialog:save_file", registry.Handlers.Keys);
        Assert.Contains("dialog:open_folder", registry.Handlers.Keys);
    }

    private sealed class FakeDialogs : ICarbonDialogs
    {
        public (string, string) LastMessage { get; private set; }
        public (string, string) LastConfirm { get; private set; }
        public (string, string?, bool) LastOpen { get; private set; }

        public Task MessageAsync(string title, string message)
        {
            LastMessage = (title, message);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            LastConfirm = (title, message);
            return Task.FromResult(true);
        }

        public Task<string[]?> OpenFileAsync(string title, string? defaultPath, bool multiple, string[]? filters)
        {
            LastOpen = (title, defaultPath, multiple);
            return Task.FromResult<string[]?>(["/picked.txt"]);
        }

        public Task<string?> SaveFileAsync(string title, string? defaultPath) =>
            Task.FromResult<string?>("/saved.txt");

        public Task<string?> OpenFolderAsync(string title, string? defaultPath) =>
            Task.FromResult<string?>("/folder");
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
