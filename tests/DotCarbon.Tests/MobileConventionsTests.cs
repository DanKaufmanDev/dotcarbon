using DotCarbon.Cli.Platforms;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.6: mobile webview conventions. Orientation is declarative, so it must reach the Android
/// activity attribute and the iOS supported-orientations list. (Safe-area insets and the Android back
/// button live in the hosts and are verified on a device.)
/// </summary>
public class MobileConventionsTests
{
    [Fact]
    public void Portrait_locks_both_platforms_to_portrait()
    {
        var (activity, plist) = Generate("portrait");

        Assert.Contains("ScreenOrientation = ScreenOrientation.Portrait", activity);
        Assert.Contains("UIInterfaceOrientationPortrait", plist);
        Assert.DoesNotContain("UIInterfaceOrientationLandscape", plist);
    }

    [Fact]
    public void Landscape_locks_both_platforms_to_landscape()
    {
        var (activity, plist) = Generate("landscape");

        Assert.Contains("ScreenOrientation = ScreenOrientation.Landscape", activity);
        Assert.Contains("UIInterfaceOrientationLandscapeLeft", plist);
        Assert.Contains("UIInterfaceOrientationLandscapeRight", plist);
        Assert.DoesNotContain("<string>UIInterfaceOrientationPortrait</string>", plist);
    }

    [Fact]
    public void The_default_leaves_orientation_to_the_system()
    {
        var (activity, plist) = Generate("any");

        // No attribute at all on Android; every orientation offered on iOS.
        Assert.DoesNotContain("ScreenOrientation =", activity);
        Assert.Contains("UIInterfaceOrientationPortrait", plist);
        Assert.Contains("UIInterfaceOrientationLandscapeLeft", plist);
    }

    [Fact]
    public void Orientation_is_part_of_the_sync_signature()
    {
        // Otherwise `carbon platform sync` would not regenerate when the orientation changes.
        var portrait = Fixtures.App();
        portrait.Window.Orientation = "portrait";
        var landscape = Fixtures.App();
        landscape.Window.Orientation = "landscape";

        Assert.NotEqual(
            new AndroidPlatformGenerator().ConfigSignature(portrait),
            new AndroidPlatformGenerator().ConfigSignature(landscape));
        Assert.NotEqual(
            new IosPlatformGenerator().ConfigSignature(portrait),
            new IosPlatformGenerator().ConfigSignature(landscape));
    }

    private static (string Activity, string Plist) Generate(string orientation)
    {
        var config = Fixtures.App(name: "MobileSmoke", version: "0.1.0", id: "dev.dotcarbon.mobilesmoke");
        config.Window.Orientation = orientation;

        var activity = new AndroidPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "MainActivity.cs").Content;
        var plist = new IosPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "Info.plist").Content;

        return (activity, plist);
    }
}
