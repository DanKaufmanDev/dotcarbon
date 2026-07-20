using DotCarbon.Plugins.Path;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.8: resource_dir must point at where the bundler actually put bundle.resources, which differs
/// per platform. These pin each branch of the resolver so the frontend's resolveResource can find files.
/// </summary>
public class PathResourceDirTests
{
    [Fact]
    public void Env_override_wins_over_everything()
    {
        // Linux launchers export CARBON_RESOURCE_DIR to the packaged location; it takes precedence.
        var resolved = PathPlugin.ResolveResourceRoot("/opt/app/usr/lib/app", "/anything", isMacOs: true);
        Assert.Equal("/opt/app/usr/lib/app", resolved);
    }

    [Fact]
    public void MacOs_app_bundle_resolves_the_sibling_resources_dir()
    {
        // Executable runs from Contents/MacOS; resources live in the sibling Contents/Resources.
        var resolved = PathPlugin.ResolveResourceRoot(null, "/Apps/My.app/Contents/MacOS/", isMacOs: true);
        Assert.Equal(Path.Combine("/Apps/My.app/Contents", "Resources"), resolved);
    }

    [Fact]
    public void Non_bundle_dir_falls_back_to_the_base_dir()
    {
        // A dev output dir has no resources/ subdir and isn't a .app, so it resolves to itself.
        var baseDir = Directory.CreateTempSubdirectory("carbon-res-dev-").FullName;
        try
        {
            Assert.Equal(baseDir, PathPlugin.ResolveResourceRoot(null, baseDir, isMacOs: false));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }

    [Fact]
    public void Resources_subdir_beside_the_executable_is_used_when_present()
    {
        // Windows/other bundles place a resources/ dir next to the exe.
        var baseDir = Directory.CreateTempSubdirectory("carbon-res-win-").FullName;
        try
        {
            var sub = Path.Combine(baseDir, "resources");
            Directory.CreateDirectory(sub);
            Assert.Equal(sub, PathPlugin.ResolveResourceRoot(null, baseDir, isMacOs: false));
        }
        finally { Directory.Delete(baseDir, recursive: true); }
    }
}
