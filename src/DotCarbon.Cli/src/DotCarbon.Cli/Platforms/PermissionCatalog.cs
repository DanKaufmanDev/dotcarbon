using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Platforms;

/// <summary>How one Carbon permission maps to native platform declarations.</summary>
internal sealed record PermissionMapping(
    string Id,
    IReadOnlyList<string> AndroidPermissions,
    string? IosUsageKey,
    string DefaultDescription);

/// <summary>
/// Maps Carbon device permissions (camera, microphone, …) to AndroidManifest
/// <c>&lt;uses-permission&gt;</c> entries and iOS Info.plist usage-description keys.
/// </summary>
internal static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionMapping> All =
    [
        new("camera", ["android.permission.CAMERA"], "NSCameraUsageDescription", "This app uses the camera."),
        new("microphone", ["android.permission.RECORD_AUDIO"], "NSMicrophoneUsageDescription", "This app uses the microphone."),
        new("location",
            ["android.permission.ACCESS_FINE_LOCATION", "android.permission.ACCESS_COARSE_LOCATION"],
            "NSLocationWhenInUseUsageDescription", "This app uses your location."),
        new("notifications", ["android.permission.POST_NOTIFICATIONS"], null, string.Empty),
        new("contacts", ["android.permission.READ_CONTACTS"], "NSContactsUsageDescription", "This app accesses your contacts."),
        new("photoLibrary", ["android.permission.READ_MEDIA_IMAGES"], "NSPhotoLibraryUsageDescription", "This app accesses your photo library."),
        // Haptics: a normal Android permission (no runtime prompt), and nothing to declare on iOS.
        new("vibrate", ["android.permission.VIBRATE"], null, string.Empty),
    ];

    public static bool IsEnabled(PermissionsConfig permissions, string id) => id switch
    {
        "camera" => permissions.Camera,
        "microphone" => permissions.Microphone,
        "location" => permissions.Location,
        "notifications" => permissions.Notifications,
        "contacts" => permissions.Contacts,
        "photoLibrary" => permissions.PhotoLibrary,
        "vibrate" => permissions.Vibrate,
        _ => false,
    };

    /// <summary>Enabled permission mappings, in catalog order.</summary>
    public static IEnumerable<PermissionMapping> Enabled(CarbonConfig config) =>
        All.Where(mapping => IsEnabled(config.Permissions, mapping.Id));

    /// <summary>Fully-qualified Android permission names for the enabled permissions (+ file scope).</summary>
    public static IReadOnlyList<string> AndroidUsesPermissions(CarbonConfig config)
    {
        var result = Enabled(config).SelectMany(mapping => mapping.AndroidPermissions).ToList();
        if (string.Equals(config.Permissions.Files, "external", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("android.permission.READ_EXTERNAL_STORAGE");
            result.Add("android.permission.WRITE_EXTERNAL_STORAGE");
        }
        return result.Distinct().ToList();
    }

    /// <summary>iOS Info.plist usage-description entries (key, description) for the enabled permissions.</summary>
    public static IReadOnlyList<(string Key, string Description)> IosUsageDescriptions(CarbonConfig config)
    {
        var result = new List<(string, string)>();
        foreach (var mapping in Enabled(config))
        {
            if (mapping.IosUsageKey is null) continue;
            var description = config.Permissions.Descriptions.TryGetValue(mapping.Id, out var custom) &&
                              !string.IsNullOrWhiteSpace(custom)
                ? custom
                : mapping.DefaultDescription;
            result.Add((mapping.IosUsageKey, description));
        }
        return result;
    }

    /// <summary>A stable signature of the permission inputs, for platform sync drift detection.</summary>
    public static string Signature(CarbonConfig config)
    {
        var enabled = string.Join(",", Enabled(config).Select(mapping => mapping.Id));
        var descriptions = string.Join(",",
            config.Permissions.Descriptions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{enabled}|files={config.Permissions.Files}|{descriptions}";
    }
}
