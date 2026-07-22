using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Biometric;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.5: biometric authentication. The command surface routes to an IBiometricProvider; the
/// Android (framework BiometricPrompt) and iOS (LAContext) providers are verified by building the
/// mobile TFMs and on a device. The no-provider fallback must never report success.
/// </summary>
public class BiometricPluginTests
{
    [Fact]
    public async Task Status_and_authenticate_route_to_the_provider()
    {
        var provider = new FakeBiometric(BiometricStatus.Available, new AuthenticateResult(true, null));
        var plugin = new BiometricPlugin(provider);

        Assert.Equal(BiometricStatus.Available, await plugin.Status());

        var result = await plugin.Authenticate(new AuthenticateArgs("Unlock", "Prove it", "Nope"));

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(("Unlock", "Prove it", "Nope"),
            (provider.LastArgs!.Title, provider.LastArgs.Reason, provider.LastArgs.CancelLabel));
    }

    [Fact]
    public async Task A_failed_prompt_carries_the_reason()
    {
        var plugin = new BiometricPlugin(
            new FakeBiometric(BiometricStatus.Available, new AuthenticateResult(false, "Cancelled.")));

        var result = await plugin.Authenticate(new AuthenticateArgs());

        Assert.False(result.Success);
        Assert.Equal("Cancelled.", result.Error);
    }

    [Fact]
    public async Task Without_a_native_provider_it_reports_unsupported_and_never_succeeds()
    {
        // This gates access, so a missing provider must fail closed rather than wave the user through.
        var plugin = new BiometricPlugin(new UnsupportedBiometricProvider());

        Assert.Equal(BiometricStatus.Unsupported, await plugin.Status());

        var result = await plugin.Authenticate(new AuthenticateArgs());
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new BiometricPlugin(new FakeBiometric(BiometricStatus.Available, new AuthenticateResult(true, null)))
            .Register(registry);

        Assert.Contains("biometric:status", registry.Handlers.Keys);
        Assert.Contains("biometric:authenticate", registry.Handlers.Keys);
    }

    private sealed class FakeBiometric : IBiometricProvider
    {
        private readonly string _status;
        private readonly AuthenticateResult _result;

        public FakeBiometric(string status, AuthenticateResult result)
        {
            _status = status;
            _result = result;
        }

        public AuthenticateArgs? LastArgs { get; private set; }

        public Task<string> StatusAsync() => Task.FromResult(_status);

        public Task<AuthenticateResult> AuthenticateAsync(AuthenticateArgs args)
        {
            LastArgs = args;
            return Task.FromResult(_result);
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
