using DotCarbon.Cli.Bundling;
using Xunit;

namespace DotCarbon.Tests;

public class MobileBundlerTests
{
    [Fact]
    public void Android_java_sdk_finder_accepts_android_studio_jbr()
    {
        var dir = CreateTempDir();
        try
        {
            var java = Path.Combine(dir, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            Touch(java);

            Assert.Equal(Path.GetFullPath(dir), MobileBundleSupport.FindJavaSdkDirectory([dir]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_build_files_embed_assets_and_strip_attributes_before_codesign()
    {
        var dir = CreateTempDir();
        try
        {
            var project = Touch(Path.Combine(dir, "Mobile.iOS.csproj"));
            var dist = Path.Combine(dir, "dist");
            Directory.CreateDirectory(dist);
            var config = Touch(Path.Combine(dir, "carbon.json"));

            var props = MobileBundleSupport.WriteEmbedProps(
                dir, project, dist, config, "DotCarbon.iOS.props");
            var targets = MobileBundleSupport.WriteIosCodesignTargets(dir);
            var propsXml = File.ReadAllText(props);
            var targetsXml = File.ReadAllText(targets);

            Assert.Contains("$(MSBuildProjectFile)", propsXml);
            Assert.Contains("Mobile.iOS.csproj", propsXml);
            Assert.Contains("CarbonStripExtendedAttributesBeforeCodesign", targetsXml);
            Assert.Contains("BeforeTargets=\"_CodesignAppBundle\"", targetsXml);
            Assert.Contains("/usr/bin/xattr -cr", targetsXml);
            Assert.Contains("$(AppBundleDir)", targetsXml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_dev_props_embed_config_only_so_the_host_selects_dev_server_mode()
    {
        var dir = CreateTempDir();
        try
        {
            var project = Touch(Path.Combine(dir, "Mobile.iOS.csproj"));
            var config = Touch(Path.Combine(dir, "carbon.json"));

            var props = MobileBundleSupport.WriteDevConfigProps(dir, project, config, "DotCarbon.iOS.props");
            var propsXml = File.ReadAllText(props);

            // carbon.json is embedded (the host still reads its config from the manifest)...
            Assert.Contains("DotCarbon.Config/carbon.json", propsXml);
            Assert.Contains("$(MSBuildProjectFile)", propsXml);
            // ...but the frontend is not, so EmbeddedAssetStore reports no assets and CarbonApp
            // loads the live dev server (hot reload) instead of baked assets.
            Assert.DoesNotContain("DotCarbon.Assets/", propsXml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Android_dev_props_embed_config_only_and_allow_cleartext_for_the_dev_server()
    {
        var dir = CreateTempDir();
        try
        {
            var project = Touch(Path.Combine(dir, "Mobile.Android.csproj"));
            var config = Touch(Path.Combine(dir, "carbon.json"));

            var props = MobileBundleSupport.WriteAndroidDevProps(dir, project, config, "DotCarbon.Android.props");
            var propsXml = File.ReadAllText(props);

            // config embedded, frontend not (-> DevServer mode), and a manifest overlay is wired in.
            Assert.Contains("DotCarbon.Config/carbon.json", propsXml);
            Assert.DoesNotContain("DotCarbon.Assets/", propsXml);
            Assert.Contains("AndroidManifestOverlay", propsXml);

            // the referenced overlay permits cleartext http so the WebView can reach the dev server.
            var overlay = Path.Combine(dir, "obj", "dotcarbon", "DotCarbon.Dev.AndroidManifest.xml");
            Assert.True(File.Exists(overlay));
            Assert.Contains("usesCleartextTraffic", File.ReadAllText(overlay));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_dev_ats_injection_allows_the_plaintext_localhost_dev_server()
    {
        if (!OperatingSystem.IsMacOS()) return; // PlistBuddy is macOS-only

        var dir = CreateTempDir();
        try
        {
            var plist = Path.Combine(dir, "Info.plist");
            File.WriteAllText(plist,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" " +
                "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\"><dict>\n" +
                "<key>CFBundleIdentifier</key><string>dev.example.app</string>\n" +
                "</dict></plist>\n");

            MobileBundleSupport.InjectLocalNetworkingAts(dir);

            var xml = File.ReadAllText(plist);
            Assert.Contains("NSAppTransportSecurity", xml);
            Assert.Contains("NSAllowsLocalNetworking", xml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_project_staging_rewrites_project_references_and_excludes_old_outputs()
    {
        var dir = CreateTempDir();
        try
        {
            var iosDir = Path.Combine(dir, "source", "ios");
            var sharedProject = Touch(Path.Combine(dir, "source", "shared", "AppLogic.csproj"));
            var project = Path.Combine(iosDir, "Mobile.iOS.csproj");
            Directory.CreateDirectory(iosDir);
            File.WriteAllText(project,
                "<Project><ItemGroup><ProjectReference Include=\"../shared/AppLogic.csproj\" />" +
                "</ItemGroup></Project>");
            Touch(Path.Combine(iosDir, "Main.cs"));
            Touch(Path.Combine(iosDir, "bin", "stale.dll"));
            Touch(Path.Combine(iosDir, "obj", "stale.g.cs"));

            var stagedProject = IosBundler.StageProject(iosDir, project, Path.Combine(dir, "cache"));
            var stagedDir = Path.GetDirectoryName(stagedProject)!;
            var xml = File.ReadAllText(stagedProject);

            Assert.True(File.Exists(Path.Combine(stagedDir, "Main.cs")));
            Assert.False(File.Exists(Path.Combine(stagedDir, "bin", "stale.dll")));
            Assert.False(File.Exists(Path.Combine(stagedDir, "obj", "stale.g.cs")));
            Assert.Contains(Path.GetFullPath(sharedProject), xml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Android_artifact_locator_prefers_signed_bin_apk()
    {
        var dir = CreateTempDir();
        try
        {
            var objApk = Touch(Path.Combine(dir, "obj", "Debug", "net10.0-android", "android", "bin", "dev.example.app.apk"));
            var unsignedApk = Touch(Path.Combine(dir, "bin", "Debug", "net10.0-android", "dev.example.app.apk"));
            var signedApk = Touch(Path.Combine(dir, "bin", "Debug", "net10.0-android", "dev.example.app-Signed.apk"));

            File.SetLastWriteTimeUtc(objApk, DateTime.UtcNow.AddMinutes(2));
            File.SetLastWriteTimeUtc(unsignedApk, DateTime.UtcNow.AddMinutes(1));
            File.SetLastWriteTimeUtc(signedApk, DateTime.UtcNow);

            Assert.Equal(signedApk, AndroidBundler.LocateArtifact(dir, "apk", "Debug"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_artifact_locator_ignores_obj_codesign_app()
    {
        var dir = CreateTempDir();
        try
        {
            var binApp = Directory.CreateDirectory(
                Path.Combine(dir, "bin", "Debug", "net10.0-ios", "iossimulator-arm64", "MobileSmoke.iOS.app")).FullName;
            var objApp = Directory.CreateDirectory(
                Path.Combine(dir, "obj", "Debug", "net10.0-ios", "iossimulator-arm64", "codesign", "MobileSmoke.iOS.app")).FullName;

            Directory.SetLastWriteTimeUtc(binApp, DateTime.UtcNow);
            Directory.SetLastWriteTimeUtc(objApp, DateTime.UtcNow.AddMinutes(1));

            Assert.Equal(binApp, IosBundler.LocateArtifact(dir, archive: false, configuration: "Debug"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_artifact_publisher_preserves_executable_mode()
    {
        if (OperatingSystem.IsWindows()) return;

        var dir = CreateTempDir();
        try
        {
            var app = Path.Combine(dir, "build", "MobileSmoke.iOS.app");
            var executable = Touch(Path.Combine(app, "MobileSmoke.iOS"));
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(executable, mode);

            var published = IosBundler.PublishArtifact(app, dir, "simulator");
            var publishedExecutable = Path.Combine(published, "MobileSmoke.iOS");

            Assert.True(File.Exists(publishedExecutable));
            Assert.Equal(mode, File.GetUnixFileMode(publishedExecutable));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Ios_app_bundle_validation_requires_a_top_level_executable()
    {
        if (OperatingSystem.IsWindows()) return;

        var dir = CreateTempDir();
        try
        {
            var app = Path.Combine(dir, "MobileSmoke.iOS.app");
            Touch(Path.Combine(app, "Info.plist"));

            Assert.False(IosBundler.HasBundleExecutable(app));

            var executable = Touch(Path.Combine(app, "MobileSmoke.iOS"));
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);

            Assert.True(IosBundler.HasBundleExecutable(app));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dotcarbon-mobile-bundler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "artifact");
        return path;
    }
}
