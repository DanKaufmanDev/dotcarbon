using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Bridge;

/// <summary>
/// A one-way stream from a command to the frontend (Task 4.1 — Tauri's <c>Channel&lt;T&gt;</c>). The
/// frontend creates a channel and passes it in a command's arguments; the command sends any number of
/// messages through it while it runs, which arrive at the channel's <c>onmessage</c> in order.
///
/// A channel is deserialized from the frontend's <c>{ "__carbon_channel__": id }</c> marker by
/// <see cref="CarbonChannelConverter"/>, which captures the window making the call so the messages go
/// back to the right webview. Because the capture happens during argument deserialization — inside the
/// command's invocation scope — no generator support is needed; a channel is just an argument field.
/// </summary>
[JsonConverter(typeof(CarbonChannelConverter))]
public sealed class CarbonChannel
{
    private readonly CarbonWindow? _window;

    internal CarbonChannel(long id, CarbonWindow? window)
    {
        Id = id;
        _window = window;
    }

    /// <summary>The frontend channel id.</summary>
    public long Id { get; }

    /// <summary>Send a typed message to the frontend. AOT-safe via the supplied type metadata.</summary>
    public Task SendAsync<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        if (_window is null) return Task.CompletedTask;
        var node = JsonSerializer.SerializeToNode(value, typeInfo);
        return _window.SendChannelMessageAsync(Id, node);
    }

    /// <summary>Send an already-serialized message to the frontend.</summary>
    public Task SendAsync(JsonNode? message) =>
        _window is null ? Task.CompletedTask : _window.SendChannelMessageAsync(Id, message);
}

/// <summary>
/// Reads a <see cref="CarbonChannel"/> from the frontend's channel marker and binds it to the window
/// currently handling the command, so its messages route back to that webview. Manual reader use keeps
/// it AOT/trim-clean. Channels are input-only, so writing is unsupported.
/// </summary>
internal sealed class CarbonChannelConverter : JsonConverter<CarbonChannel>
{
    public override CarbonChannel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var id = 0L;
        if (reader.TokenType == JsonTokenType.Number)
        {
            id = reader.GetInt64();
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var name = reader.GetString();
                reader.Read();
                if (name == "__carbon_channel__" && reader.TokenType == JsonTokenType.Number)
                    id = reader.GetInt64();
            }
        }

        return new CarbonChannel(id, CarbonInvocationScope.Current?.Window);
    }

    public override void Write(Utf8JsonWriter writer, CarbonChannel value, JsonSerializerOptions options) =>
        throw new NotSupportedException("A channel cannot be sent back to the frontend.");
}

/// <summary>
/// The command context currently being handled, tracked so a <see cref="CarbonChannel"/> deserialized
/// from arguments can find its window. Set by the runtime around each invocation.
/// </summary>
internal static class CarbonInvocationScope
{
    private static readonly AsyncLocal<CarbonCommandContext?> Slot = new();

    public static CarbonCommandContext? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }
}
