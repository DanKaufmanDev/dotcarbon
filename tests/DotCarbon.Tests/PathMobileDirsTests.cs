using DotCarbon.Plugins.Path;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.4: app directories on mobile. A mobile app already runs in a per-app sandbox
/// (Android /data/user/0/&lt;pkg&gt;, the iOS app container), so the desktop convention of nesting under
/// the app identifier would bury files a level deeper for no isolation benefit.
/// </summary>
public class PathMobileDirsTests
{
    [Fact]
    public void Desktop_nests_app_dirs_under_the_identifier()
    {
        var root = Path.Combine(Path.DirectorySeparatorChar.ToString(), "data", "roots");

        Assert.Equal(
            Path.Combine(root, "com.example.app"),
            PathPlugin.AppScopedDir(root, "com.example.app", isMobile: false));
    }

    [Fact]
    public void Mobile_uses_the_sandbox_root_directly()
    {
        const string sandbox = "/data/user/0/com.example.app/files";

        // No second "com.example.app" segment — the sandbox is already app-scoped.
        Assert.Equal(sandbox, PathPlugin.AppScopedDir(sandbox, "com.example.app", isMobile: true));
    }

    [Fact]
    public void Mobile_sandbox_root_keeps_no_trailing_separator()
    {
        Assert.Equal(
            "/data/user/0/com.example.app/files",
            PathPlugin.AppScopedDir("/data/user/0/com.example.app/files/", "com.example.app", isMobile: true));
    }
}
