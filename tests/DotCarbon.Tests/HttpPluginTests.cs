using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCarbon.Plugins.Http;
using Xunit;

namespace DotCarbon.Tests;

public class HttpPluginTests
{
    [Fact]
    public void Empty_scope_allows_any_url()
    {
        Assert.True(HttpScope.IsAllowed("https://anything.example/x", Array.Empty<string>()));
    }

    [Theory]
    [InlineData("https://api.example.com/v1/users", "https://api.example.com/*", true)]
    [InlineData("https://api.example.com/v1/users?active=true", "https://api.example.com/v1/*", true)]
    [InlineData("https://api.example.com", "https://api.example.com", true)]
    [InlineData("https://api.example.com.evil/v1/users", "https://api.example.com/*", false)]
    [InlineData("https://evil.com/steal", "https://api.example.com/*", false)]
    [InlineData("http://api.example.com/x", "https://api.example.com/*", false)]
    [InlineData("file:///tmp/secret", "https://api.example.com/*", false)]
    public void Scope_matches_prefixes_and_wildcards(string url, string pattern, bool allowed)
    {
        Assert.Equal(allowed, HttpScope.IsAllowed(url, new[] { pattern }));
    }

    [Fact]
    public async Task Fetch_returns_status_body_and_headers()
    {
        var port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var payload = Encoding.UTF8.GetBytes("pong");
            context.Response.StatusCode = 200;
            context.Response.AddHeader("X-Carbon", "yes");
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.Close();
        });

        var result = await new HttpPlugin().Fetch(new FetchArgs(prefix));
        await serve;

        Assert.Equal(200, result.Status);
        Assert.Equal("pong", result.Body);
        Assert.Equal("yes", result.Headers["X-Carbon"]);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
