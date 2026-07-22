namespace DotCarbon.Plugins.Biometric;

/// <summary>Whether biometric authentication can be used right now.</summary>
public static class BiometricStatus
{
    /// <summary>Hardware present and at least one credential enrolled.</summary>
    public const string Available = "available";
    /// <summary>Hardware present but nothing enrolled — the user must set up a fingerprint/face first.</summary>
    public const string NotEnrolled = "notEnrolled";
    /// <summary>The device has no biometric hardware.</summary>
    public const string NoHardware = "noHardware";
    /// <summary>Hardware exists but is temporarily unavailable (e.g. locked out after failures).</summary>
    public const string Unavailable = "unavailable";
    /// <summary>This platform/OS version does not support biometrics at all.</summary>
    public const string Unsupported = "unsupported";
}

/// <summary>
/// Prompt copy. <see cref="Reason"/> is what iOS shows under the Face ID/Touch ID sheet; Android shows
/// <see cref="Title"/> plus <see cref="Reason"/> as the subtitle, with <see cref="CancelLabel"/> on the
/// negative button (Android requires one).
/// </summary>
public record AuthenticateArgs(
    string Title = "Authenticate",
    string Reason = "Confirm your identity",
    string CancelLabel = "Cancel");

/// <summary>Outcome of a prompt. <see cref="Error"/> is null when <see cref="Success"/> is true.</summary>
public record AuthenticateResult(bool Success, string? Error);

/// <summary>
/// Biometric authentication. Mobile-only: a mobile app registers the native provider (Android
/// <c>BiometricPrompt</c> / iOS <c>LAContext</c>) via <c>app.UseBiometric()</c> from
/// <c>DotCarbon.Plugins.Biometric.Native</c>.
/// </summary>
public interface IBiometricProvider
{
    Task<string> StatusAsync();
    Task<AuthenticateResult> AuthenticateAsync(AuthenticateArgs args);
}
