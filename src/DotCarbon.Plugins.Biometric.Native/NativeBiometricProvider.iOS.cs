#if IOS
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Biometric;
using LocalAuthentication;

namespace DotCarbon.Plugins.Biometric.Native;

/// <summary>
/// iOS biometrics (Face ID / Touch ID) via <see cref="LAContext"/>. Using Face ID also requires an
/// <c>NSFaceIDUsageDescription</c> entry in Info.plist, otherwise the system terminates the app.
/// </summary>
internal sealed class NativeBiometricProvider : IBiometricProvider
{
    public NativeBiometricProvider(AppHandle app) { }

    public Task<string> StatusAsync()
    {
        using var context = new LAContext();
        if (context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out var error))
            return Task.FromResult(BiometricStatus.Available);

        return Task.FromResult((LAStatus)(long)(error?.Code ?? 0) switch
        {
            LAStatus.BiometryNotEnrolled => BiometricStatus.NotEnrolled,
            LAStatus.BiometryNotAvailable => BiometricStatus.NoHardware,
            _ => BiometricStatus.Unavailable,
        });
    }

    public async Task<AuthenticateResult> AuthenticateAsync(AuthenticateArgs args)
    {
        using var context = new LAContext { LocalizedCancelTitle = args.CancelLabel };

        var (succeeded, error) = await context.EvaluatePolicyAsync(
            LAPolicy.DeviceOwnerAuthenticationWithBiometrics, args.Reason);

        return succeeded
            ? new AuthenticateResult(true, null)
            : new AuthenticateResult(false, error?.LocalizedDescription ?? "Authentication failed.");
    }
}
#endif
