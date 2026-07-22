using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Biometric;

/// <summary>
/// Face ID / Touch ID / Android BiometricPrompt. Check <c>status</c> before prompting so the UI can
/// explain why the option is missing (no hardware vs nothing enrolled).
/// </summary>
[CarbonPlugin("Biometric", description: "Authenticate the user with Face ID / Touch ID / BiometricPrompt.")]
[CarbonPluginPlatform("android", "ios")]
[CarbonPermission("biometric:default", "Allow biometric authentication prompts.", Commands = new[] { "biometric:*" })]
public partial class BiometricPlugin : IPlugin
{
    private readonly IBiometricProvider _provider;

    public BiometricPlugin(AppHandle app)
        : this(app.Services.GetService<IBiometricProvider>() ?? new UnsupportedBiometricProvider()) { }

    // Injection seam for tests and for the native binding.
    internal BiometricPlugin(IBiometricProvider provider) => _provider = provider;

    public string Namespace => "biometric";

    [CarbonCommand("status")]
    public Task<string> Status() => _provider.StatusAsync();

    [CarbonCommand("authenticate")]
    public Task<AuthenticateResult> Authenticate(AuthenticateArgs args) => _provider.AuthenticateAsync(args);
}
