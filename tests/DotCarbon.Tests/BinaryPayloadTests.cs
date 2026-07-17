using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using Xunit;

namespace DotCarbon.Tests;

// A plugin returning CarbonBinary registers it in its serializer context, exactly like this. The
// [JsonConverter] on CarbonBinary means source-gen routes through the URL-parking converter.
[JsonSerializable(typeof(CarbonBinary))]
internal partial class BinaryTestContext : JsonSerializerContext;

/// <summary>
/// Task 4.2: a CarbonBinary result is parked and returned as a carbon:// URL; fetching that URL over
/// the asset pipeline yields the exact bytes, once (the entry is one-shot).
/// </summary>
public class BinaryPayloadTests
{
    [Fact]
    public void Binary_result_serializes_to_a_fetchable_carbon_url()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x10, 0x42, 0x7F, 0x80, 0xAB };
        var binary = new CarbonBinary(bytes, "application/octet-stream");

        // Serializing the result parks the bytes and yields the carbon:// URL (what the frontend gets).
        var url = JsonSerializer.Serialize(binary).Trim('"');
        Assert.StartsWith("carbon://localhost/__binary__/", url);

        // Serving that URL returns the exact bytes with the content type.
        var response = CarbonAssets.Serve(url);
        Assert.Equal("application/octet-stream", response.ContentType);
        using var ms = new MemoryStream();
        response.Content.CopyTo(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public void Binary_entry_is_one_shot()
    {
        var url = JsonSerializer.Serialize(new CarbonBinary([1, 2, 3])).Trim('"');

        var first = ReadAll(CarbonAssets.Serve(url));
        Assert.Equal(new byte[] { 1, 2, 3 }, first);

        // Already taken: a second fetch of the same token does not return the bytes.
        var second = ReadAll(CarbonAssets.Serve(url));
        Assert.NotEqual(new byte[] { 1, 2, 3 }, second);
    }

    [Fact]
    public void Binary_works_through_a_source_generated_context()
    {
        // The path a real generated command takes: SerializeToNode via the plugin's context.
        var node = JsonSerializer.SerializeToNode(
            new CarbonBinary([9, 8, 7], "image/png"), BinaryTestContext.Default.CarbonBinary);

        var url = node!.GetValue<string>();
        Assert.StartsWith("carbon://localhost/__binary__/", url);
        var response = CarbonAssets.Serve(url);
        Assert.Equal("image/png", response.ContentType);
        Assert.Equal(new byte[] { 9, 8, 7 }, ReadAll(response));
    }

    private static byte[] ReadAll(CarbonAssetResponse response)
    {
        using var ms = new MemoryStream();
        response.Content.CopyTo(ms);
        return ms.ToArray();
    }
}
