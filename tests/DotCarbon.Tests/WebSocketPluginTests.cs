using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.WebSocket;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.7: client websockets run in the backend and stream received frames to the frontend as
/// websocket:message events. This drives a real round-trip against an in-process echo server.
/// </summary>
public class WebSocketPluginTests
{
    private static CarbonConfig Config() => new() { Window = new WindowConfig { Label = "main" } };

    private static async Task<WebApplication> StartEchoServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/echo", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    break;
                }
                await socket.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<bool> WaitForFrame(NoopWebView view, string contains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            string[] snapshot;
            try { snapshot = [.. view.Sent]; }
            catch (InvalidOperationException) { await Task.Delay(20); continue; } // list mutated mid-copy
            if (snapshot.Any(message => message.Contains("websocket:message") && message.Contains(contains)))
                return true;
            await Task.Delay(25);
        }
        return false;
    }

    [Fact]
    public async Task Connect_send_and_receive_an_echoed_frame()
    {
        var server = await StartEchoServer();
        var wsUrl = server.Urls.First().Replace("http://", "ws://") + "/echo";

        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        var plugin = new WebSocketPlugin(handle);
        try
        {
            var id = await plugin.Connect(new WsConnectArgs(wsUrl));
            await plugin.Send(new WsSendArgs(id, "hello-carbon"));

            // The backend receives the echo and forwards it as a websocket:message event to the webview.
            Assert.True(
                await WaitForFrame(host.Views["main"], "hello-carbon", TimeSpan.FromSeconds(5)),
                "did not receive the echoed frame as a websocket:message event");

            await plugin.Disconnect(new WsDisconnectArgs(id));
        }
        finally
        {
            await plugin.DisposeAsync();
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
            new WebSocketPlugin(handle).Register(registry);

            Assert.Contains("websocket:connect", registry.Handlers.Keys);
            Assert.Contains("websocket:send", registry.Handlers.Keys);
            Assert.Contains("websocket:disconnect", registry.Handlers.Keys);
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
