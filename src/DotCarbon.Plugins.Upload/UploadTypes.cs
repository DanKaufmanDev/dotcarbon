namespace DotCarbon.Plugins.Upload;

/// <summary>
/// Upload a local file to <c>Url</c>. <c>Id</c> correlates the progress events (the frontend picks it so
/// it can subscribe before the transfer starts). <c>Method</c> defaults to POST.
/// </summary>
public record UploadArgs(
    string Url,
    string FilePath,
    long Id,
    Dictionary<string, string>? Headers = null,
    string? Method = null);

/// <summary>Download <c>Url</c> to a local file. <c>Id</c> correlates the progress events.</summary>
public record DownloadArgs(
    string Url,
    string FilePath,
    long Id,
    Dictionary<string, string>? Headers = null);

/// <summary>Transfer progress: bytes moved so far and the total (–1 when unknown).</summary>
public record ProgressPayload(long Id, long Progress, long Total);
