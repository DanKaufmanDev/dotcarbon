using AVFoundation;
using DotCarbon.Core.Host;
using UserNotifications;

namespace DotCarbon.Host.iOS;

/// <summary>
/// iOS authorization. Unlike Android's single runtime-permission API, iOS gates each capability through
/// its own framework, so this maps Carbon's permission ids onto the matching per-API calls. Ids without
/// a mapping here report as unsupported rather than silently succeeding.
/// </summary>
internal sealed class IosPermissions : ICarbonPermissions
{
    public async Task<string> StatusAsync(string permission) => Normalize(permission) switch
    {
        "notifications" => await NotificationStatusAsync(),
        "camera" => MediaStatus(AVAuthorizationMediaType.Video),
        "microphone" => MediaStatus(AVAuthorizationMediaType.Audio),
        _ => CarbonPermissionState.Unsupported,
    };

    public async Task<string> RequestAsync(string permission)
    {
        switch (Normalize(permission))
        {
            case "notifications":
                var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
                    UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
                return granted ? CarbonPermissionState.Granted : CarbonPermissionState.Denied;

            case "camera":
                return await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video)
                    ? CarbonPermissionState.Granted
                    : CarbonPermissionState.Denied;

            case "microphone":
                return await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Audio)
                    ? CarbonPermissionState.Granted
                    : CarbonPermissionState.Denied;

            default:
                return CarbonPermissionState.Unsupported;
        }
    }

    private static string Normalize(string permission) => permission.Trim().ToLowerInvariant();

    private static async Task<string> NotificationStatusAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional => CarbonPermissionState.Granted,
            UNAuthorizationStatus.Denied => CarbonPermissionState.Denied,
            _ => CarbonPermissionState.Prompt,
        };
    }

    private static string MediaStatus(AVAuthorizationMediaType mediaType) =>
        AVCaptureDevice.GetAuthorizationStatus(mediaType) switch
        {
            AVAuthorizationStatus.Authorized => CarbonPermissionState.Granted,
            AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted => CarbonPermissionState.Denied,
            _ => CarbonPermissionState.Prompt,
        };
}
