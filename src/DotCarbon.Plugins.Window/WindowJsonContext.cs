using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Window;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SetTitleArgs))]
[JsonSerializable(typeof(SetSizeArgs))]
[JsonSerializable(typeof(SetPositionArgs))]
[JsonSerializable(typeof(SetFullscreenArgs))]
[JsonSerializable(typeof(SetAlwaysOnTopArgs))]
[JsonSerializable(typeof(SetResizableArgs))]
[JsonSerializable(typeof(TargetWindowArgs))]
[JsonSerializable(typeof(CreateWindowArgs))]
[JsonSerializable(typeof(SetFlagArgs))]
[JsonSerializable(typeof(SetIconArgs))]
[JsonSerializable(typeof(SetCursorIconArgs))]
[JsonSerializable(typeof(SetThemeArgs))]
[JsonSerializable(typeof(SetMinSizeArgs))]
[JsonSerializable(typeof(SetMaxSizeArgs))]
[JsonSerializable(typeof(WindowSize))]
[JsonSerializable(typeof(WindowPosition))]
[JsonSerializable(typeof(MonitorInfo))]
[JsonSerializable(typeof(List<MonitorInfo>))]
[JsonSerializable(typeof(WindowState))]
[JsonSerializable(typeof(List<WindowState>))]
internal partial class WindowJsonContext : JsonSerializerContext;
