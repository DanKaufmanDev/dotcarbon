using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Notification;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SendNotificationArgs))]
internal partial class NotificationJsonContext : JsonSerializerContext;
