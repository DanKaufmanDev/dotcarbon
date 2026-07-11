using System.Text;
using System.Text.Json;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Security;

internal sealed class BridgeSecurityPolicy
{
    private const string RuntimeOrigin = "carbon://localhost";
    private readonly CarbonConfig _config;
    private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        RuntimeOrigin,
    };

    public BridgeSecurityPolicy(CarbonConfig config)
    {
        _config = config;
    }

    public void Configure(bool isDevServer)
    {
        _allowedOrigins.Clear();
        _allowedOrigins.Add(RuntimeOrigin);

        if (isDevServer)
            _allowedOrigins.Add(GetOrigin(_config.Build.DevUrl));

        foreach (var origin in _config.Security.AllowedOrigins)
            _allowedOrigins.Add(GetOrigin(origin));
    }

    public void EnsureNavigationAllowed(Uri uri)
    {
        if (IsRuntimeOrigin(uri)) return;
        if (_allowedOrigins.Contains(GetOrigin(uri))) return;
        if (_config.Security.AllowExternalUrls) return;

        throw new UnauthorizedAccessException(
            $"Navigation to '{GetOrigin(uri)}' is blocked. Add it to security.allowedOrigins or enable security.allowExternalUrls.");
    }

    public void EnsureBridgeMessageAllowed(CarbonWindow window, string message)
    {
        if (Encoding.UTF8.GetByteCount(message) > _config.Security.MaxBridgeMessageBytes)
            throw new InvalidOperationException(
                $"Bridge message exceeds security.maxBridgeMessageBytes ({_config.Security.MaxBridgeMessageBytes}).");

        if (window.CurrentUri is null)
            throw new UnauthorizedAccessException(
                $"Bridge calls are blocked before window '{window.Label}' has a trusted URL.");

        if (!IsRuntimeOrigin(window.CurrentUri) &&
            !_allowedOrigins.Contains(GetOrigin(window.CurrentUri)))
            throw new UnauthorizedAccessException(
                $"Bridge calls from '{GetOrigin(window.CurrentUri)}' are blocked.");
    }

    public void EnsureCommandNameAllowed(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 128)
            throw new InvalidOperationException("Bridge command names must be 1-128 characters.");

        if (command.StartsWith("__carbon:", StringComparison.Ordinal) &&
            command != "__carbon:event_emit")
            throw new UnauthorizedAccessException($"Reserved Carbon command is blocked: {command}");

        if (command == "__carbon:event_emit") return;

        if (!IsCommandName(command))
            throw new InvalidOperationException(
                $"Invalid bridge command name '{command}'. Expected namespace:command.");
    }

    public void EnsureRequestIdAllowed(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            id.Any(char.IsControl))
            throw new InvalidOperationException("Bridge request ids must be 1-128 non-control characters.");
    }

    public void EnsureEventEmitPayloadAllowed(JsonElement payload)
    {
        var raw = payload.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > _config.Security.MaxEventPayloadBytes)
            throw new InvalidOperationException(
                $"Event payload exceeds security.maxEventPayloadBytes ({_config.Security.MaxEventPayloadBytes}).");

        if (!payload.TryGetProperty("event", out var eventNameElement) ||
            eventNameElement.ValueKind != JsonValueKind.String ||
            !IsEventName(eventNameElement.GetString()))
            throw new InvalidOperationException("Event names must be 1-128 characters.");

        if (!payload.TryGetProperty("target", out var target) ||
            target.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        if (target.ValueKind == JsonValueKind.String)
        {
            var value = target.GetString();
            if (value is "all" or "app" || IsLabel(value)) return;
            throw new InvalidOperationException($"Invalid event target: {value}");
        }

        if (target.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Event target must be a string or object.");

        var kind = target.TryGetProperty("kind", out var kindValue)
            ? kindValue.GetString()
            : "all";
        if (kind is "all" or "app") return;
        if (kind == "window" &&
            target.TryGetProperty("label", out var label) &&
            label.ValueKind == JsonValueKind.String &&
            IsLabel(label.GetString()))
            return;

        throw new InvalidOperationException("Invalid event target object.");
    }

    private static bool IsRuntimeOrigin(Uri uri) =>
        uri.Scheme == "carbon" &&
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static string GetOrigin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? GetOrigin(uri)
            : throw new InvalidOperationException($"Invalid origin URL: {url}");

    private static string GetOrigin(Uri uri)
    {
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port);
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static bool IsCommandName(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1 || value.IndexOf(':', colon + 1) >= 0)
            return false;

        return IsIdentifier(value[..colon]) && IsIdentifier(value[(colon + 1)..]);
    }

    private static bool IsEventName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is ':' or '.' or '-' or '_');

    private static bool IsLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 80 &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static bool IsIdentifier(string value) =>
        value.Length > 0 &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');
}
