using DotCarbon.Cli.Bundling;
using Xunit;

namespace DotCarbon.Tests;

public class MobileBundlerTests
{
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
