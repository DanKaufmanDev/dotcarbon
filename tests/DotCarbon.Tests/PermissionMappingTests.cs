using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

public class PermissionMappingTests
{
    private static CarbonConfig WithPermissions(Action<PermissionsConfig> configure)
    {
        var config = Fixtures.App();
        configure(config.Permissions);
        return config;
    }

    [Fact]
    public void Camera_maps_to_android_permission_and_ios_usage_key()
    {
        var config = WithPermissions(p => p.Camera = true);

        Assert.Contains("android.permission.CAMERA", PermissionCatalog.AndroidUsesPermissions(config));
        Assert.Contains(PermissionCatalog.IosUsageDescriptions(config), e => e.Key == "NSCameraUsageDescription");
    }

    [Fact]
    public void External_files_scope_adds_storage_permissions()
    {
        var config = WithPermissions(p => p.Files = "external");
        var android = PermissionCatalog.AndroidUsesPermissions(config);

        Assert.Contains("android.permission.READ_EXTERNAL_STORAGE", android);
        Assert.Contains("android.permission.WRITE_EXTERNAL_STORAGE", android);
    }

    [Fact]
    public void Custom_usage_description_overrides_the_default()
    {
        var config = WithPermissions(p =>
        {
            p.Camera = true;
            p.Descriptions["camera"] = "Scan receipts";
        });

        var camera = PermissionCatalog.IosUsageDescriptions(config).Single(e => e.Key == "NSCameraUsageDescription");
        Assert.Equal("Scan receipts", camera.Description);
    }

    [Fact]
    public void A_default_usage_description_is_used_when_none_is_set()
    {
        var config = WithPermissions(p => p.Microphone = true);

        var mic = PermissionCatalog.IosUsageDescriptions(config).Single(e => e.Key == "NSMicrophoneUsageDescription");
        Assert.False(string.IsNullOrWhiteSpace(mic.Description));
    }

    [Fact]
    public void No_permissions_maps_to_nothing()
    {
        var config = Fixtures.App();
        Assert.Empty(PermissionCatalog.AndroidUsesPermissions(config));
        Assert.Empty(PermissionCatalog.IosUsageDescriptions(config));
    }
}
