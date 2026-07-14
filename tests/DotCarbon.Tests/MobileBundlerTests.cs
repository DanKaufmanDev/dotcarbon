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
    public void Ios_build_files_redirect_output_and_strip_attributes_before_codesign()
    {
        var dir = CreateTempDir();
        try
        {
            var project = Touch(Path.Combine(dir, "Mobile.iOS.csproj"));
            var dist = Path.Combine(dir, "dist");
            Directory.CreateDirectory(dist);
            var config = Touch(Path.Combine(dir, "carbon.json"));

            var props = MobileBundleSupport.WriteEmbedProps(
                dir,
                project,
                dist,
                config,
                "DotCarbon.iOS.props",
                baseOutputPath: Path.Combine(dir, "local-output"));
            var targets = MobileBundleSupport.WriteIosCodesignTargets(dir);
            var propsXml = File.ReadAllText(props);
            var targetsXml = File.ReadAllText(targets);

            Assert.Contains("<BaseOutputPath>", propsXml);
            Assert.Contains("local-output", propsXml);
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
