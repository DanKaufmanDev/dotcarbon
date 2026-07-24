using System.Text.Json;

namespace DotCarbon.Plugins.Updater;

public record UpdaterStatus(bool Active, string CurrentVersion, string[] Endpoints, bool HasPublicKey);

public record CheckUpdateArgs(string? Endpoint = null);

public record UpdateCheckResult(bool Available, string CurrentVersion, string? LatestVersion, string? Endpoint, JsonElement? Manifest);

public record DownloadUpdateArgs(string? Endpoint = null, string? DestinationDir = null);

public record InstallUpdateArgs(string? Path = null, string? Endpoint = null, bool Restart = false);

public record UpdateManifest(
    string Version,
    string Target,
    string Url,
    string? Artifact = null,
    string? Signature = null,
    string? PublicKey = null,
    string? Algorithm = null,
    string? Sha256 = null,
    long? Size = null);

/// <summary>
/// Emitted as the <c>updater:download-progress</c> event while an update downloads. <see cref="Total"/>
/// is null when the server does not send a Content-Length; <see cref="Percent"/> is then 0.
/// </summary>
public record UpdateProgress(long Downloaded, long? Total, double Percent);

public record UpdateDownloadResult(
    bool Available,
    string CurrentVersion,
    string LatestVersion,
    string Path,
    string FileName,
    string Sha256,
    bool SignatureVerified,
    UpdateManifest Manifest);

public record UpdateInstallResult(
    string Path,
    bool Started,
    bool RestartRequested,
    string Message);
