using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 9.5: Flatpak and Snap packaging. The manifest generators are pure, so their content — app id,
/// launch command, and the sandbox permissions a WebKitGTK app needs — is covered here. Running
/// flatpak-builder/snapcraft happens on Linux with those tools installed (this box has neither), so
/// the generated manifest is a ready-to-build recipe rather than a built package.
/// </summary>
public class LinuxPackagingTests
{
    private static CarbonConfig Config(Action<CarbonConfig>? customize = null)
    {
        var config = new CarbonConfig
        {
            App = { Name = "Demo App", Version = "1.4.2", Identifier = "com.example.demo" },
        };
        customize?.Invoke(config);
        return config;
    }

    [Fact]
    public void The_flatpak_manifest_carries_the_app_id_command_and_runtime()
    {
        var manifest = LinuxPackaging.FlatpakManifest(Config(), "demo-app", "DemoApp");

        Assert.Contains("app-id: com.example.demo", manifest);
        Assert.Contains("command: demo-app", manifest);
        Assert.Contains("runtime: org.freedesktop.Platform", manifest);
        Assert.Contains("sdk: org.freedesktop.Sdk", manifest);
    }

    [Fact]
    public void The_flatpak_sandbox_grants_what_a_webview_app_needs()
    {
        // A WebKitGTK app is a black screen without a GPU device and a display socket, and Carbon apps
        // do network and notifications — so these finish-args are load-bearing, not decoration.
        var manifest = LinuxPackaging.FlatpakManifest(Config(), "demo-app", "DemoApp");

        Assert.Contains("--device=dri", manifest);
        Assert.Contains("--socket=wayland", manifest);
        Assert.Contains("--socket=fallback-x11", manifest);
        Assert.Contains("--share=network", manifest);
        Assert.Contains("--talk-name=org.freedesktop.Notifications", manifest);
    }

    [Fact]
    public void The_flatpak_module_installs_the_payload_and_the_launcher()
    {
        var manifest = LinuxPackaging.FlatpakManifest(Config(), "demo-app", "DemoApp");

        Assert.Contains("cp -r payload/* /app/lib/demo-app/", manifest);
        Assert.Contains("install -Dm755 launcher /app/bin/demo-app", manifest);
        // The .desktop is installed under the app id, which is what Flatpak requires.
        Assert.Contains("/app/share/applications/com.example.demo.desktop", manifest);
    }

    [Fact]
    public void The_flatpak_launcher_points_at_the_payload_and_sets_the_resource_dir()
    {
        var launcher = LinuxPackaging.FlatpakLauncher("demo-app", "DemoApp");

        Assert.StartsWith("#!/bin/sh", launcher);
        Assert.Contains("CARBON_RESOURCE_DIR=\"/app/lib/demo-app\"", launcher);
        Assert.Contains("exec \"/app/lib/demo-app/DemoApp\"", launcher);
    }

    [Fact]
    public void The_snapcraft_recipe_names_the_app_and_its_launch_command()
    {
        var yaml = LinuxPackaging.SnapcraftYaml(Config(), "demo-app", "DemoApp");

        Assert.Contains("name: demo-app", yaml);
        Assert.Contains("version: '1.4.2'", yaml);
        Assert.Contains("command: bin/demo-app", yaml);
        Assert.Contains("base: core24", yaml);
        Assert.Contains("confinement: strict", yaml);
    }

    [Fact]
    public void The_snap_plugs_cover_display_network_and_audio()
    {
        var yaml = LinuxPackaging.SnapcraftYaml(Config(), "demo-app", "DemoApp");

        foreach (var plug in new[] { "wayland", "x11", "opengl", "network", "audio-playback", "browser-support" })
            Assert.Contains($"- {plug}", yaml);
    }

    [Fact]
    public void The_snap_part_dumps_the_payload_and_writes_a_launch_wrapper()
    {
        var yaml = LinuxPackaging.SnapcraftYaml(Config(), "demo-app", "DemoApp");

        Assert.Contains("plugin: dump", yaml);
        Assert.Contains("source: payload/", yaml);
        // The wrapper resolves the app under $SNAP at runtime.
        Assert.Contains("CARBON_RESOURCE_DIR=\"$SNAP/lib/demo-app\"", yaml);
        Assert.Contains("$SNAP/lib/demo-app/DemoApp", yaml);
    }

    [Fact]
    public void A_long_app_name_is_truncated_to_snaps_summary_limit()
    {
        // snapcraft rejects a summary longer than 78 characters, which would fail the whole pack.
        var yaml = LinuxPackaging.SnapcraftYaml(
            Config(c => c.App.Name = new string('x', 200)), "demo-app", "DemoApp");

        var summaryLine = yaml.Split('\n').First(line => line.StartsWith("summary:"));
        Assert.True(summaryLine.Length - "summary: ".Length <= 78);
    }

    [Theory]
    [InlineData("flatpak")]
    [InlineData("snap")]
    public void The_new_formats_are_valid_for_linux(string format)
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "linux-x64", [format]));
        Assert.Equal([format], config.Bundle.Linux.Formats);
    }
}
