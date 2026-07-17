using System.Text.Json;
using System.Text.Json.Serialization;
using DotCarbon.Core.Host;

namespace DotCarbon.Core.Bridge;

/// <summary>
/// A binary command result delivered without base64 overhead (Task 4.2). Return one from a command;
/// the bytes are parked in <see cref="CarbonBinaryStore"/> and the result the frontend receives is a
/// <c>carbon://</c> URL it fetches to get the raw bytes (the api's <c>readBinary</c> helper does this).
///
/// This is the efficient path for large payloads; a plain <c>byte[]</c> still works but rides through
/// the JSON channel as base64.
/// </summary>
[JsonConverter(typeof(CarbonBinaryConverter))]
public sealed class CarbonBinary(byte[] data, string contentType = "application/octet-stream")
{
    public byte[] Data { get; } = data;
    public string ContentType { get; } = contentType;
}

/// <summary>
/// Serializes a <see cref="CarbonBinary"/> by parking its bytes and writing the <c>carbon://</c> URL
/// that serves them. Output-only: a channel/binary result never arrives from the frontend, so reading
/// is unsupported.
/// </summary>
internal sealed class CarbonBinaryConverter : JsonConverter<CarbonBinary>
{
    public override CarbonBinary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Binary payloads are results, not arguments.");

    public override void Write(Utf8JsonWriter writer, CarbonBinary value, JsonSerializerOptions options) =>
        writer.WriteStringValue(CarbonBinaryStore.Register(value.Data, value.ContentType));
}
