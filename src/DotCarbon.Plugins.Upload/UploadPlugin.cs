using System.Net;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Upload;

/// <summary>
/// Chunked file upload and download with progress events (Task 6.8). Transfers stream through the
/// backend and report progress as <c>upload:progress</c> / <c>download:progress</c> events, keyed by the
/// id the caller supplies. Mirrors Tauri's upload plugin.
/// </summary>
[CarbonPlugin("Upload", description: "Chunked file upload and download with progress events.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("upload:default", "Allow all upload commands.", Commands = new[] { "upload:*" })]
[CarbonEvent("upload:progress", "ProgressPayload", "Bytes uploaded so far for a transfer.")]
[CarbonEvent("download:progress", "ProgressPayload", "Bytes downloaded so far for a transfer.")]
public partial class UploadPlugin : IPlugin
{
    private const int ChunkSize = 64 * 1024;
    private static readonly HttpClient Http = new();

    private readonly AppHandle _app;

    public UploadPlugin(AppHandle app) => _app = app;

    public string Namespace => "upload";

    /// <summary>Upload a file; returns the server's response body.</summary>
    [CarbonCommand("upload")]
    public async Task<string> Upload(UploadArgs args)
    {
        var length = new FileInfo(args.FilePath).Length;
        using var request = new HttpRequestMessage(new HttpMethod(args.Method ?? "POST"), args.Url);
        AddHeaders(request, args.Headers);

        var stream = File.OpenRead(args.FilePath);
        request.Content = new ProgressContent(stream, length,
            transferred => Emit("upload:progress", new ProgressPayload(args.Id, transferred, length)));

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Download a URL to a local file.</summary>
    [CarbonCommand("download")]
    public async Task Download(DownloadArgs args)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, args.Url);
        AddHeaders(request, args.Headers);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = File.Create(args.FilePath);

        var buffer = new byte[ChunkSize];
        long transferred = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            transferred += read;
            await Emit("download:progress", new ProgressPayload(args.Id, transferred, total));
        }
    }

    private Task Emit(string eventName, ProgressPayload payload) =>
        _app.EmitAsync(new CarbonEventName<ProgressPayload>(eventName), payload, UploadJsonContext.Default.ProgressPayload);

    private static void AddHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
    }

    /// <summary>Streams a file to the request body in chunks, reporting the running byte count.</summary>
    private sealed class ProgressContent(Stream source, long length, Func<long, Task> onProgress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[ChunkSize];
            long transferred = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
                transferred += read;
                await onProgress(transferred);
            }
        }

        protected override bool TryComputeLength(out long computedLength)
        {
            computedLength = length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
    }
}
