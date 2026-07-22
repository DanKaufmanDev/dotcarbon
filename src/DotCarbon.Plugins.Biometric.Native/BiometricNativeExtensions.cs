using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Biometric;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Biometric.Native;

/// <summary>
/// Registers the platform-native <see cref="IBiometricProvider"/> so <c>BiometricPlugin</c> prompts with
/// real biometrics. Call before <c>Start()</c>:
/// <code>app.UseBiometric().UsePlugin&lt;BiometricPlugin&gt;();</code>
/// iOS additionally needs an <c>NSFaceIDUsageDescription</c> in Info.plist to use Face ID.
/// </summary>
public static class BiometricNativeExtensions
{
    public static CarbonApp UseBiometric(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<IBiometricProvider>(sp =>
                new NativeBiometricProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
