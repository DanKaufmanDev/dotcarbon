using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Upload;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.8: chunked upload/download stream through the backend and report progress events. These drive
/// real transfers against an in-process server, checking both the bytes and that progress reaches total.
/// </summary>
public class UploadPluginTests : IDisposable
{
    private readonly string _dir;
    // 200 KB so the 64 KB chunking fires several progress events.
    private readonly byte[] _payload = MakePayload(200_000);

    public UploadPluginTests() => _dir = Directory.CreateTempSubdirectory("carbon-upload-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CarbonConfig Config() => new() { Window = new WindowConfig { Label = "main" } };

    private static byte[] MakePayload(int size)
    {
        var bytes = new byte[size];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private async Task<(WebApplication server, string baseUrl, List<byte[]> uploaded)> StartServer()
    {
        var uploaded = new List<byte[]>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapGet("/download", async context =>
        {
            context.Response.ContentLength = _payload.Length;
            await context.Response.Body.WriteAsync(_payload);
        });
        app.MapPost("/upload", async context =>
        {
            using var memory = new MemoryStream();
            await context.Request.Body.CopyToAsync(memory);
            lock (uploaded) uploaded.Add(memory.ToArray());
            await context.Response.WriteAsync(memory.Length.ToString());
        });

        await app.StartAsync();
        return (app, app.Urls.First(), uploaded);
    }

    private static async Task<bool> WaitForProgress(NoopWebView view, string eventName, long total, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var completed = $"\"progress\":{total}"; // the final event: progress == total
        while (DateTime.UtcNow < deadline)
        {
            string[] snapshot;
            try { snapshot = [.. view.Sent]; }
            catch (InvalidOperationException) { await Task.Delay(20); continue; }
            if (snapshot.Any(message => message.Contains(eventName) && message.Contains(completed)))
                return true;
            await Task.Delay(25);
        }
        return false;
    }

    [Fact]
    public async Task Download_writes_the_file_and_reports_progress()
    {
        var (server, baseUrl, _) = await StartServer();
        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        var plugin = new UploadPlugin(handle);
        var target = Path.Combine(_dir, "got.bin");
        try
        {
            await plugin.Download(new DownloadArgs($"{baseUrl}/download", target, Id: 1));

            Assert.Equal(_payload, await File.ReadAllBytesAsync(target));
            Assert.True(
                await WaitForProgress(host.Views["main"], "download:progress", _payload.Length, TimeSpan.FromSeconds(5)),
                "download progress never reached total");
        }
        finally
        {
            app.Shutdown();
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Upload_sends_the_file_and_reports_progress()
    {
        var (server, baseUrl, uploaded) = await StartServer();
        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        var plugin = new UploadPlugin(handle);

        var source = Path.Combine(_dir, "send.bin");
        await File.WriteAllBytesAsync(source, _payload);
        try
        {
            var response = await plugin.Upload(new UploadArgs($"{baseUrl}/upload", source, Id: 2));

            Assert.Equal(_payload.Length.ToString(), response);       // server echoed the byte count
            Assert.Equal(_payload, Assert.Single(uploaded));          // server received the exact bytes
            Assert.True(
                await WaitForProgress(host.Views["main"], "upload:progress", _payload.Length, TimeSpan.FromSeconds(5)),
                "upload progress never reached total");
        }
        finally
        {
            app.Shutdown();
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public void Registers_its_commands()
    {
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var registry = new FakeRegistry();
            new UploadPlugin(handle).Register(registry);

            Assert.Contains("upload:upload", registry.Handlers.Keys);
            Assert.Contains("upload:download", registry.Handlers.Keys);
        }
        finally { app.Shutdown(); }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
