using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Notification;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.4: Notifications ported to the mobile native-binding pattern. The command surface routes to
/// an INotificationProvider; the Android/iOS providers (DotCarbon.Plugins.Notification.Native) are
/// verified by building the mobile TFMs and on a device.
/// </summary>
public class NotificationPluginTests
{
    [Fact]
    public async Task Send_routes_to_the_provider()
    {
        var provider = new FakeNotifier();
        var plugin = new NotificationPlugin(provider);

        await plugin.Send(new SendNotificationArgs("Title", "Body", "Subtitle"));

        var sent = Assert.Single(provider.Sent);
        Assert.Equal(("Title", "Body", "Subtitle"), (sent.Title, sent.Body, sent.Subtitle));
    }

    [Fact]
    public void Registers_send_command()
    {
        var registry = new FakeRegistry();
        new NotificationPlugin(new FakeNotifier()).Register(registry);

        Assert.Contains("notification:send", registry.Handlers.Keys);
    }

    private sealed class FakeNotifier : INotificationProvider
    {
        public List<SendNotificationArgs> Sent { get; } = [];
        public Task Send(SendNotificationArgs args) { Sent.Add(args); return Task.CompletedTask; }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
