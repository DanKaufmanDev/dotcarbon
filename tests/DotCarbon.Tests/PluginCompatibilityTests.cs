using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

public class PluginCompatibilityTests
{
    [Fact]
    public void Shell_plugin_is_desktop_only()
    {
        Assert.Equal(new[] { "desktop" }, AddCommand.Catalog["shell"].EffectivePlatforms);
    }

    [Fact]
    public void FileSystem_plugin_supports_all_platforms()
    {
        Assert.Equal(new[] { "desktop", "android", "ios" }, AddCommand.Catalog["fs"].EffectivePlatforms);
    }

    [Fact]
    public void Discover_finds_referenced_plugins_from_csproj()
    {
        var dir = Fixtures.TempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
              <PackageReference Include="DotCarbon.Plugins.Shell" Version="*" />
              <PackageReference Include="DotCarbon.Plugins.FileSystem" Version="*" />
            </ItemGroup></Project>
            """);

        var plugins = PluginCompatibility.Discover(dir);

        Assert.Contains(plugins, p => p.Namespace == "shell");
        Assert.Contains(plugins, p => p.Namespace == "fs");
    }

    [Fact]
    public void Incompatible_flags_desktop_only_plugins_for_mobile_targets()
    {
        var dir = Fixtures.TempProject(
            """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="DotCarbon.Plugins.Shell" Version="*" /></ItemGroup></Project>""");

        Assert.Contains(PluginCompatibility.Incompatible(dir, "android"), p => p.Namespace == "shell");
        Assert.Contains(PluginCompatibility.Incompatible(dir, "ios"), p => p.Namespace == "shell");
        Assert.Empty(PluginCompatibility.Incompatible(dir, "desktop"));
    }

    [Fact]
    public void Cross_platform_plugin_is_never_incompatible()
    {
        var dir = Fixtures.TempProject(
            """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="DotCarbon.Plugins.FileSystem" Version="*" /></ItemGroup></Project>""");

        Assert.Empty(PluginCompatibility.Incompatible(dir, "android"));
        Assert.Empty(PluginCompatibility.Incompatible(dir, "ios"));
    }
}
