using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Updater;

[CarbonPlugin("Updater", description: "Check signed updater manifests.")]
[CarbonPermission("updater:default", "Allow updater commands.", Commands = new[] { "updater:*" })]
public partial class UpdaterPlugin : IPlugin
{
    private readonly CarbonConfig _config;
    private readonly HttpClient _http = new();

    public UpdaterPlugin(CarbonConfig config)
    {
        _config = config;
    }

    public string Namespace => "updater";

    [CarbonCommand("status")]
    public UpdaterStatus Status() =>
        new(
            _config.Bundle.Updater.Active,
            _config.App.Version,
            _config.Bundle.Updater.Endpoints.ToArray(),
            !string.IsNullOrWhiteSpace(_config.Bundle.Updater.PublicKey));

    [CarbonCommand("check")]
    public async Task<UpdateCheckResult> Check(CheckUpdateArgs args)
    {
        var endpoint = args.Endpoint ?? _config.Bundle.Updater.Endpoints.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(endpoint))
            return new UpdateCheckResult(false, _config.App.Version, null, null, null);

        using var response = await _http.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var manifest = document.RootElement.Clone();
        var latest = TryGetVersion(manifest);
        return new UpdateCheckResult(
            latest is not null && !string.Equals(latest, _config.App.Version, StringComparison.OrdinalIgnoreCase),
            _config.App.Version,
            latest,
            endpoint,
            manifest);
    }

    private static string? TryGetVersion(JsonElement manifest)
    {
        if (manifest.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
            return version.GetString();
        if (manifest.TryGetProperty("app", out var app) &&
            app.TryGetProperty("version", out var appVersion) &&
            appVersion.ValueKind == JsonValueKind.String)
            return appVersion.GetString();
        return null;
    }
}
