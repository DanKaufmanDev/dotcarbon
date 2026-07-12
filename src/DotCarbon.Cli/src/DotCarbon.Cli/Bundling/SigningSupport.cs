using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Signing configuration lives in carbon.json + environment variables. This centralizes the env
/// var names, builds the platform signing build args, and validates that required secrets are set.
/// </summary>
internal static class SigningSupport
{
    public const string AndroidKeystorePasswordEnv = "CARBON_ANDROID_KEYSTORE_PASSWORD";
    public const string AndroidKeyPasswordEnv = "CARBON_ANDROID_KEY_PASSWORD";
    public const string MacIdentityEnv = "APPLE_SIGNING_IDENTITY";
    public const string MacNotarizationEnv = "APPLE_NOTARIZATION_PROFILE";
    public const string UpdaterKeyEnv = "CARBON_UPDATER_PRIVATE_KEY";

    /// <summary>
    /// Android release signing args, or <c>false</c> with an actionable error if a keystore is
    /// configured but incomplete (missing alias / password / file). No keystore → debug signing (ok).
    /// </summary>
    public static bool TryAndroidSigningArgs(
        CarbonConfig config, string workingDir, bool release, out string args, out string error)
    {
        args = string.Empty;
        error = string.Empty;

        var signing = config.Bundle.Android.Signing;
        if (string.IsNullOrWhiteSpace(signing.Keystore) || !release)
            return true; // debug builds and unsigned configs use the debug keystore

        var keystore = Path.GetFullPath(Path.Combine(workingDir, signing.Keystore));
        if (!File.Exists(keystore))
        {
            error = $"Android keystore not found: {keystore}";
            return false;
        }
        if (string.IsNullOrWhiteSpace(signing.KeyAlias))
        {
            error = "bundle.android.signing.keyAlias is required when a keystore is set.";
            return false;
        }

        var storePassword = Environment.GetEnvironmentVariable(AndroidKeystorePasswordEnv);
        if (string.IsNullOrWhiteSpace(storePassword))
        {
            error = $"Set {AndroidKeystorePasswordEnv} to sign the release keystore.";
            return false;
        }
        var keyPassword = Environment.GetEnvironmentVariable(AndroidKeyPasswordEnv) ?? storePassword;

        args =
            $"-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=\"{keystore}\" " +
            $"-p:AndroidSigningKeyAlias=\"{signing.KeyAlias}\" " +
            $"-p:AndroidSigningStorePass=\"{storePassword}\" -p:AndroidSigningKeyPass=\"{keyPassword}\"";
        return true;
    }

    /// <summary>iOS codesign args from bundle.ios.signing (empty for simulator/unsigned).</summary>
    public static string IosSigningArgs(CarbonConfig config)
    {
        var signing = config.Bundle.Ios.Signing;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(signing.Identity))
            parts.Add($"-p:CodesignKey=\"{signing.Identity}\"");
        if (!string.IsNullOrWhiteSpace(signing.ProvisioningProfile))
            parts.Add($"-p:CodesignProvision=\"{signing.ProvisioningProfile}\"");
        return string.Join(" ", parts);
    }

    public static bool IosCanSign(CarbonConfig config) =>
        !string.IsNullOrWhiteSpace(config.Bundle.Ios.Signing.Identity);

    public static bool HasEnv(string name) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
}
