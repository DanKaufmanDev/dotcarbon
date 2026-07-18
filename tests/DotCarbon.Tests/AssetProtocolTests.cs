using System.Text;
using DotCarbon.Core.Host;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.6: convertFileSrc serves a scoped local file over carbon://. A file inside an allowed root
/// is served; anything outside is forbidden, so the webview can't reach the whole filesystem.
/// </summary>
public class AssetProtocolTests : IDisposable
{
    private readonly string _root;

    public AssetProtocolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "carbon-asset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CarbonAssetScope.Configure([_root]);
    }

    public void Dispose()
    {
        CarbonAssetScope.Configure([]);
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // Mirrors @dotcarbon/api convertFileSrc.
    private static string ConvertFileSrc(string path) =>
        "carbon://localhost/__asset__/" + Uri.EscapeDataString(path);

    [Fact]
    public void In_scope_file_is_served_with_content_type()
    {
        // A name with a space checks the encode/decode round-trip.
        var file = Path.Combine(_root, "my photo.png");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF };
        File.WriteAllBytes(file, bytes);

        var response = CarbonAssets.Serve(ConvertFileSrc(file));

        Assert.Equal("image/png", response.ContentType);
        using var ms = new MemoryStream();
        response.Content.CopyTo(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public void Out_of_scope_file_is_forbidden()
    {
        var outside = Path.Combine(Path.GetTempPath(), "carbon-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "secret");
        try
        {
            var response = CarbonAssets.Serve(ConvertFileSrc(outside));
            Assert.Equal("text/plain", response.ContentType);
            using var reader = new StreamReader(response.Content);
            Assert.Equal("Forbidden", reader.ReadToEnd());
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void Path_traversal_out_of_scope_is_forbidden()
    {
        // ../ escapes the root even though the URL starts inside it.
        var escape = Path.Combine(_root, "..", "etc-passwd-" + Guid.NewGuid().ToString("N"));
        var response = CarbonAssets.Serve(ConvertFileSrc(escape));
        using var reader = new StreamReader(response.Content);
        Assert.Equal("Forbidden", reader.ReadToEnd());
    }
}
