using System.Text.Json;

namespace DotCarbon.Plugins.Updater;

public record UpdaterStatus(bool Active, string CurrentVersion, string[] Endpoints, bool HasPublicKey);

public record CheckUpdateArgs(string? Endpoint = null);

public record UpdateCheckResult(bool Available, string CurrentVersion, string? LatestVersion, string? Endpoint, JsonElement? Manifest);
