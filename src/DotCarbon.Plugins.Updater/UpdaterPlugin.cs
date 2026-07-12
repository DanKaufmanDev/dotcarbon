using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Updater;

[CarbonPlugin("Updater", description: "Check signed updater manifests.")]
[CarbonPluginPlatform("desktop")]
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
        var endpoint = ResolveEndpoint(args.Endpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
            return new UpdateCheckResult(false, _config.App.Version, null, null, null);

        using var document = await LoadManifestDocument(endpoint);
        var manifest = document.RootElement.Clone();
        var latest = TryGetVersion(manifest);
        return new UpdateCheckResult(
            latest is not null && !string.Equals(latest, _config.App.Version, StringComparison.OrdinalIgnoreCase),
            _config.App.Version,
            latest,
            endpoint,
            manifest);
    }

    [CarbonCommand("download")]
    public async Task<UpdateDownloadResult> Download(DownloadUpdateArgs args)
    {
        var endpoint = ResolveEndpoint(args.Endpoint)
            ?? throw new InvalidOperationException("No updater endpoint configured.");
        var manifest = await LoadManifest(endpoint);
        var latest = manifest.Version;
        var available = !string.Equals(latest, _config.App.Version, StringComparison.OrdinalIgnoreCase);
        var fileName = SafeFileName(manifest.Artifact) ?? FileNameFromUrl(manifest.Url)
            ?? $"update-{latest}";
        var destination = args.DestinationDir is null
            ? StagingPath(latest, fileName)
            : Path.Combine(Path.GetFullPath(args.DestinationDir), fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await DownloadArtifact(ResolveArtifactUrl(endpoint, manifest.Url), destination);

        var sha256 = await Sha256FileAsync(destination);
        if (!string.IsNullOrWhiteSpace(manifest.Sha256) &&
            !sha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Downloaded update hash does not match manifest sha256.");

        var signatureVerified = false;
        if (!string.IsNullOrWhiteSpace(manifest.Signature))
        {
            VerifySignature(destination, manifest);
            signatureVerified = true;
        }

        return new UpdateDownloadResult(
            available,
            _config.App.Version,
            latest,
            destination,
            fileName,
            sha256,
            signatureVerified,
            manifest);
    }

    [CarbonCommand("install")]
    public async Task<UpdateInstallResult> Install(InstallUpdateArgs args)
    {
        var path = args.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            var downloaded = await Download(new DownloadUpdateArgs(args.Endpoint));
            path = downloaded.Path;
        }

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Update artifact does not exist: {path}");

        StartInstaller(path);
        if (args.Restart)
            Environment.Exit(0);

        return new UpdateInstallResult(
            path,
            Started: true,
            RestartRequested: args.Restart,
            Message: args.Restart
                ? "Started installer and requested application exit."
                : "Started installer. Restart the app after the installer finishes.");
    }

    [CarbonCommand("install_and_restart")]
    public Task<UpdateInstallResult> InstallAndRestart(InstallUpdateArgs args) =>
        Install(args with { Restart = true });

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

    private string? ResolveEndpoint(string? endpoint)
    {
        endpoint ??= _config.Bundle.Updater.Endpoints.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        return endpoint
            .Replace("{{target}}", CurrentTarget(), StringComparison.Ordinal)
            .Replace("{{version}}", _config.App.Version, StringComparison.Ordinal);
    }

    private async Task<UpdateManifest> LoadManifest(string endpoint)
    {
        using var document = await LoadManifestDocument(endpoint);
        return document.RootElement.Deserialize(UpdaterJsonContext.Default.UpdateManifest)
            ?? throw new InvalidOperationException("Updater manifest could not be parsed.");
    }

    private async Task<JsonDocument> LoadManifestDocument(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsFile)
            return JsonDocument.Parse(await File.ReadAllTextAsync(uri.LocalPath));

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            return JsonDocument.Parse(await File.ReadAllTextAsync(Path.GetFullPath(endpoint)));

        using var response = await _http.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private async Task DownloadArtifact(string url, string destination)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            File.Copy(uri.LocalPath, destination, overwrite: true);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            File.Copy(Path.GetFullPath(url), destination, overwrite: true);
            return;
        }

        using var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        await input.CopyToAsync(output);
    }

    private void VerifySignature(string path, UpdateManifest manifest)
    {
        var publicKey = _config.Bundle.Updater.PublicKey ?? manifest.PublicKey;
        if (string.IsNullOrWhiteSpace(publicKey))
            throw new CryptographicException("Updater manifest is signed but no public key is configured.");
        if (!string.IsNullOrWhiteSpace(_config.Bundle.Updater.PublicKey) &&
            !string.IsNullOrWhiteSpace(manifest.PublicKey) &&
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(_config.Bundle.Updater.PublicKey),
                Convert.FromBase64String(manifest.PublicKey)))
            throw new CryptographicException("Updater manifest public key does not match carbon.json.");

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        var bytes = File.ReadAllBytes(path);
        var signature = Convert.FromBase64String(manifest.Signature!);
        if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new CryptographicException("Updater artifact signature is invalid.");
    }

    private static void StartInstaller(string path)
    {
        if (OperatingSystem.IsMacOS())
            Process.Start("open", new[] { path });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            throw new PlatformNotSupportedException("Updater install is only supported on desktop platforms.");
    }

    private string StagingPath(string version, string fileName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DotCarbon",
            Sanitize(_config.App.Identifier),
            "updates",
            Sanitize(version),
            fileName);

    private static string ResolveArtifactUrl(string endpoint, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            return new Uri(endpointUri, url).ToString();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(endpoint))!, url));
    }

    private static string CurrentTarget() =>
        OperatingSystem.IsMacOS() ? "osx" :
        OperatingSystem.IsWindows() ? "win" :
        OperatingSystem.IsLinux() ? "linux" :
        "desktop";

    private static async Task<string> Sha256FileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? FileNameFromUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return SafeFileName(uri.LocalPath);
        return SafeFileName(value);
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
}
