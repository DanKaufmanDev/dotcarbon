namespace DotCarbon.Core.Host;

/// <summary>
/// Runtime permission checks and prompts, provided by the platform host (prompting needs the Activity /
/// UIApplication the host owns). Mobile gates dangerous capabilities behind a user prompt — Android's
/// runtime permissions, iOS's per-API authorization — so a plugin that needs the camera, location or
/// notifications asks through here first. Desktop hosts provide none, and callers treat that as granted.
/// <para>
/// Permission ids match the ones in <c>carbon.json</c>'s <c>permissions</c> block: "camera",
/// "microphone", "location", "notifications", "contacts", "photoLibrary".
/// </para>
/// </summary>
public interface ICarbonPermissions
{
    /// <summary>
    /// Current state without prompting: <see cref="CarbonPermissionState.Granted"/>,
    /// <see cref="CarbonPermissionState.Denied"/>, <see cref="CarbonPermissionState.Prompt"/> (not asked
    /// yet) or <see cref="CarbonPermissionState.Unsupported"/> (unknown id / not applicable here).
    /// </summary>
    Task<string> StatusAsync(string permission);

    /// <summary>
    /// Prompt for the permission if it has not been decided, and report the resulting state. Already
    /// granted or permanently denied permissions return immediately without a prompt.
    /// </summary>
    Task<string> RequestAsync(string permission);
}

/// <summary>The permission states used by <see cref="ICarbonPermissions"/>.</summary>
public static class CarbonPermissionState
{
    public const string Granted = "granted";
    public const string Denied = "denied";
    public const string Prompt = "prompt";
    public const string Unsupported = "unsupported";
}
