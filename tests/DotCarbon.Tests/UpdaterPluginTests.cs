using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Updater;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Exercises update checks, downloads, and signature verification against a local HTTP server.
/// Installer launch remains covered by platform smoke tests because it terminates the test process.
/// </summary>
public class UpdaterPluginTests
{
    private const string CurrentVersion = "1.0.0";
    private const string LatestVersion = "2.0.0";

    [Fact]
    public void Status_reports_config()
    {
        var config = MakeConfig(publicKey: "abc", endpoint: "https://example/u.json");
        var status = Plugin(config).Status();

        Assert.True(status.Active);
        Assert.Equal(CurrentVersion, status.CurrentVersion);
        Assert.Equal(new[] { "https://example/u.json" }, status.Endpoints);
        Assert.True(status.HasPublicKey);
    }

    [Fact]
    public async Task Check_reports_a_newer_version_as_available()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("carbon-update-payload");
        using var server = UpdateServer.Start(key, artifact, LatestVersion, tamper: false);

        var plugin = Plugin(MakeConfig(PublicKeyBase64(key), server.ManifestUrl));
        var result = await plugin.Check(new CheckUpdateArgs());

        Assert.True(result.Available);
        Assert.Equal(CurrentVersion, result.CurrentVersion);
        Assert.Equal(LatestVersion, result.LatestVersion);
        Assert.NotNull(result.Manifest);
    }

    [Fact]
    public async Task Check_reports_no_update_when_versions_match()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("same-version");
        using var server = UpdateServer.Start(key, artifact, CurrentVersion, tamper: false);

        var result = await Plugin(MakeConfig(PublicKeyBase64(key), server.ManifestUrl))
            .Check(new CheckUpdateArgs());

        Assert.False(result.Available);
    }

    [Fact]
    public async Task Download_verifies_hash_and_signature_and_writes_the_artifact()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("carbon-update-payload-v2");
        using var server = UpdateServer.Start(key, artifact, LatestVersion, tamper: false);
        var dest = Path.Combine(Path.GetTempPath(), "carbon-updater-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            var plugin = Plugin(MakeConfig(PublicKeyBase64(key), server.ManifestUrl));
            var result = await plugin.Download(new DownloadUpdateArgs(DestinationDir: dest));

            Assert.True(result.Available);
            Assert.Equal(LatestVersion, result.LatestVersion);
            Assert.True(result.SignatureVerified);
            Assert.Equal(Sha256Hex(artifact), result.Sha256);
            Assert.True(File.Exists(result.Path));
            Assert.Equal(artifact, await File.ReadAllBytesAsync(result.Path));
        }
        finally
        {
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        }
    }

    [Fact]
    public async Task Download_emits_progress_events_to_the_webview()
    {
        // A payload larger than the copy buffer so several progress steps occur, not just start+finish.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = new byte[500_000];
        Random.Shared.NextBytes(artifact);
        using var server = UpdateServer.Start(key, artifact, LatestVersion, tamper: false);
        var dest = Path.Combine(Path.GetTempPath(), "carbon-updater-test-" + Guid.NewGuid().ToString("N"));

        var host = new RecordingHost();
        var app = CarbonApp.Create(MakeConfig(PublicKeyBase64(key), server.ManifestUrl)).UsePlatform(host);
        var handle = app.Start();
        try
        {
            await new UpdaterPlugin(handle).Download(new DownloadUpdateArgs(DestinationDir: dest));

            var progress = host.Views["main"].Sent
                .Where(message => message.Contains("updater:download-progress"))
                .ToList();

            // Several events, not just one, and the last reports the whole artifact at 100%.
            Assert.True(progress.Count >= 2, $"expected multiple progress events, got {progress.Count}");
            Assert.Contains($"\"downloaded\":{artifact.Length}", progress[^1].Replace(" ", string.Empty));
            Assert.Contains("\"percent\":100", progress[^1].Replace(" ", string.Empty));

            // Downloaded bytes never decrease across events.
            var counts = progress.Select(DownloadedBytes).ToList();
            for (var i = 1; i < counts.Count; i++)
                Assert.True(counts[i] >= counts[i - 1], "progress went backwards");
        }
        finally
        {
            app.Shutdown();
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        }
    }

    private static long DownloadedBytes(string eventMessage)
    {
        var marker = "\"downloaded\":";
        var start = eventMessage.Replace(" ", string.Empty).IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var compact = eventMessage.Replace(" ", string.Empty);
        var end = compact.IndexOf(',', start);
        return long.Parse(compact[start..end]);
    }

    [Fact]
    public async Task Download_rejects_a_tampered_artifact()
    {
        // The server swaps the advertised artifact to prove verification rejects changed bytes.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = Encoding.UTF8.GetBytes("trusted-payload");
        using var server = UpdateServer.Start(key, artifact, LatestVersion, tamper: true);
        var dest = Path.Combine(Path.GetTempPath(), "carbon-updater-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            var plugin = Plugin(MakeConfig(PublicKeyBase64(key), server.ManifestUrl));
            await Assert.ThrowsAsync<CryptographicException>(
                () => plugin.Download(new DownloadUpdateArgs(DestinationDir: dest)));
        }
        finally
        {
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        }
    }

    [Fact]
    public async Task Install_rejects_a_missing_artifact_path()
    {
        var config = MakeConfig(publicKey: "abc", endpoint: "https://example/u.json");
        var plugin = Plugin(config);
        var missing = Path.Combine(Path.GetTempPath(), "carbon-missing-" + Guid.NewGuid().ToString("N") + ".bin");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => plugin.Install(new InstallUpdateArgs(Path: missing)));
    }

    /// <summary>Builds the plugin over a real AppHandle (which it needs to emit progress events).</summary>
    private static UpdaterPlugin Plugin(CarbonConfig config) =>
        new(CarbonApp.Create(config).UsePlatform(new NoopHost()).Start());

    private static CarbonConfig MakeConfig(string? publicKey, string endpoint)
    {
        var config = new CarbonConfig();
        config.App.Version = CurrentVersion;
        config.App.Identifier = "com.dotcarbon.updater.test";
        config.Bundle.Updater.Active = true;
        config.Bundle.Updater.Endpoints = [endpoint];
        config.Bundle.Updater.PublicKey = publicKey;
        return config;
    }

    private static string PublicKeyBase64(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>A loopback HTTP server that serves a signed manifest + its artifact.</summary>
    private sealed class UpdateServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public string ManifestUrl { get; }

        private UpdateServer(HttpListener listener, string manifestUrl)
        {
            _listener = listener;
            ManifestUrl = manifestUrl;
        }

        public static UpdateServer Start(ECDsa key, byte[] artifact, string version, bool tamper)
        {
            var sha256 = Sha256Hex(artifact);
            var signature = Convert.ToBase64String(
                key.SignData(artifact, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

            var port = FreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            // Published manifests use camelCase keys.
            var manifest = $$"""
                {
                  "version": "{{version}}",
                  "target": "osx",
                  "url": "update.bin",
                  "artifact": "update.bin",
                  "sha256": "{{sha256}}",
                  "signature": "{{signature}}"
                }
                """;

            // Keep the signed metadata unchanged while replacing the response body.
            var served = tamper ? Encoding.UTF8.GetBytes("evil-swapped-payload!!") : artifact;

            var server = new UpdateServer(listener, prefix + "manifest.json");
            _ = server.ServeAsync(manifest, served);
            return server;
        }

        private async Task ServeAsync(string manifest, byte[] artifact)
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }

                var path = context.Request.Url!.AbsolutePath;
                byte[] body;
                if (path.EndsWith("manifest.json", StringComparison.Ordinal))
                {
                    body = Encoding.UTF8.GetBytes(manifest);
                    context.Response.ContentType = "application/json";
                }
                else
                {
                    body = artifact;
                    context.Response.ContentType = "application/octet-stream";
                }

                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
        }
    }
}
