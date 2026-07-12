using DotCarbon.Cli.Commands;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DotCarbon.Tests;

public class AssetTests
{
    [Fact]
    public void Generates_android_mipmaps_and_a_splash()
    {
        var (source, dir) = SetupIcon();

        Assert.True(IconCommand.GeneratePlatform(source, dir, "android", out var error), error);

        foreach (var density in new[] { "mipmap-mdpi", "mipmap-hdpi", "mipmap-xhdpi", "mipmap-xxhdpi", "mipmap-xxxhdpi" })
            Assert.True(File.Exists(Path.Combine(dir, "Resources", density, "appicon.png")), density);
        Assert.True(File.Exists(Path.Combine(dir, "Resources", "drawable", "splash.png")));

        using var xxxhdpi = Image.Load(Path.Combine(dir, "Resources", "mipmap-xxxhdpi", "appicon.png"));
        Assert.Equal(192, xxxhdpi.Width);
    }

    [Fact]
    public void Generates_ios_appiconset_with_contents_json()
    {
        var (source, dir) = SetupIcon();

        Assert.True(IconCommand.GeneratePlatform(source, dir, "ios", out var error), error);

        var appIcon = Path.Combine(dir, "Assets.xcassets", "AppIcon.appiconset");
        Assert.True(File.Exists(Path.Combine(appIcon, "AppIcon-1024.png")));
        Assert.True(File.Exists(Path.Combine(appIcon, "Contents.json")));

        using var icon = Image.Load(Path.Combine(appIcon, "AppIcon-1024.png"));
        Assert.Equal(1024, icon.Width);
    }

    [Fact]
    public void Rejects_a_non_square_source()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "icon.png");
        using (var image = new Image<Rgba32>(800, 600, new Rgba32(0, 0, 0, 255)))
            image.Save(source, new PngEncoder());

        Assert.False(IconCommand.GeneratePlatform(source, dir, "android", out var error));
        Assert.Contains("square", error);
    }

    private static (string Source, string Dir) SetupIcon()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "icon.png");
        using (var image = new Image<Rgba32>(1024, 1024, new Rgba32(70, 130, 180, 255)))
            image.Save(source, new PngEncoder());
        return (source, dir);
    }
}
