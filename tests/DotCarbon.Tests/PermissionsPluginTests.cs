using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Plugins.Permissions;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.5 groundwork: the runtime-permission flow that geolocation, biometric and camera all need.
/// Prompting belongs to the host (ICarbonPermissions); the per-platform implementations are verified by
/// building and on a device. Desktop gates none of these, so it must report granted.
/// </summary>
public class PermissionsPluginTests
{
    [Fact]
    public async Task Status_and_request_route_to_the_host()
    {
        var backend = new FakePermissions(CarbonPermissionState.Prompt);
        var plugin = new PermissionsPlugin(backend);

        Assert.Equal(CarbonPermissionState.Prompt, await plugin.Status(new PermissionArgs("camera")));
        Assert.Equal("camera", backend.LastStatusQuery);

        // Requesting prompts and returns the resulting state.
        backend.RequestResult = CarbonPermissionState.Granted;
        Assert.Equal(CarbonPermissionState.Granted, await plugin.Request(new PermissionArgs("notifications")));
        Assert.Equal("notifications", backend.LastRequest);
    }

    [Fact]
    public async Task A_denied_permission_is_reported_as_denied()
    {
        var plugin = new PermissionsPlugin(new FakePermissions(CarbonPermissionState.Denied));

        Assert.Equal(CarbonPermissionState.Denied, await plugin.Status(new PermissionArgs("camera")));
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new PermissionsPlugin(new FakePermissions(CarbonPermissionState.Prompt)).Register(registry);

        Assert.Contains("permissions:status", registry.Handlers.Keys);
        Assert.Contains("permissions:request", registry.Handlers.Keys);
    }

    private sealed class FakePermissions(string initial) : ICarbonPermissions
    {
        public string? LastStatusQuery { get; private set; }
        public string? LastRequest { get; private set; }
        public string RequestResult { get; set; } = initial;

        public Task<string> StatusAsync(string permission)
        {
            LastStatusQuery = permission;
            return Task.FromResult(initial);
        }

        public Task<string> RequestAsync(string permission)
        {
            LastRequest = permission;
            return Task.FromResult(RequestResult);
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
