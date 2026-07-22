using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.4: deep links on mobile. Schemes from <c>bundle.protocols</c> must reach the generated
/// Android intent-filters and iOS CFBundleURLTypes, and the platform hosts deliver received URLs
/// through <see cref="CarbonDeepLinks"/> to the DeepLink plugin.
/// </summary>
public class DeepLinkMobileTests
{
    [Fact]
    public void Schemes_generate_android_intent_filters_and_ios_url_types()
    {
        var config = Fixtures.App(name: "MobileSmoke", version: "0.1.0", id: "dev.dotcarbon.mobilesmoke");
        config.Bundle.Protocols.Add(new ProtocolConfig { Name = "Carbon", Schemes = { "carbontest" } });

        var activity = new AndroidPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "MainActivity.cs")
            .Content;
        var plist = new IosPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "Info.plist")
            .Content;

        // Android: an <intent-filter> for ACTION_VIEW on the scheme (needs the Intent import).
        Assert.Contains("using Android.Content;", activity);
        Assert.Contains("IntentFilter(new[] { Intent.ActionView }", activity);
        Assert.Contains("DataScheme = \"carbontest\"", activity);

        // iOS: CFBundleURLTypes carrying the scheme.
        Assert.Contains("CFBundleURLTypes", plist);
        Assert.Contains("<string>carbontest</string>", plist);
    }

    [Fact]
    public void No_schemes_leaves_the_generated_files_untouched()
    {
        var config = Fixtures.App(name: "MobileSmoke", version: "0.1.0", id: "dev.dotcarbon.mobilesmoke");

        var activity = new AndroidPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "MainActivity.cs")
            .Content;
        var plist = new IosPlatformGenerator()
            .Generate(new PlatformContext(config, ".", "."))
            .Single(file => file.RelativePath == "Info.plist")
            .Content;

        Assert.DoesNotContain("IntentFilter", activity);
        Assert.DoesNotContain("CFBundleURLTypes", plist);
    }

    [Fact]
    public void Host_delivered_urls_queue_at_launch_and_stream_while_running()
    {
        CarbonDeepLinks.Reset();
        try
        {
            // Before anyone subscribes (i.e. the launch intent), the URL is queued.
            CarbonDeepLinks.Deliver("carbontest://launch");
            Assert.Equal(["carbontest://launch"], CarbonDeepLinks.Launch);

            // After subscribing (plugin initialized), later URLs are delivered live too.
            var live = new List<string>();
            CarbonDeepLinks.Subscribe(live.Add);
            CarbonDeepLinks.Deliver("carbontest://while-running");

            Assert.Equal(["carbontest://while-running"], live);
            Assert.Equal(["carbontest://launch", "carbontest://while-running"], CarbonDeepLinks.Launch);

            // Blank URLs are ignored.
            CarbonDeepLinks.Deliver(null);
            CarbonDeepLinks.Deliver("   ");
            Assert.Equal(2, CarbonDeepLinks.Launch.Count);
        }
        finally
        {
            CarbonDeepLinks.Reset();
        }
    }
}
