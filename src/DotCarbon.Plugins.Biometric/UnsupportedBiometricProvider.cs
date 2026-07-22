namespace DotCarbon.Plugins.Biometric;

/// <summary>
/// Fallback when no native provider is registered (desktop). Status reports "unsupported" so callers can
/// branch, and authenticating fails rather than pretending the user was verified — this is an auth
/// gate, so a false success would be a security bug.
/// </summary>
internal sealed class UnsupportedBiometricProvider : IBiometricProvider
{
    public Task<string> StatusAsync() => Task.FromResult(BiometricStatus.Unsupported);

    public Task<AuthenticateResult> AuthenticateAsync(AuthenticateArgs args) =>
        Task.FromResult(new AuthenticateResult(false, "Biometric authentication is not available on this platform."));
}
