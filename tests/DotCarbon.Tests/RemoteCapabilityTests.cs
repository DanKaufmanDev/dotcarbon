using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.2: content loaded from a remote origin is denied the bridge by default. A capability only
/// reaches remote content if it lists that URL under <c>remote.urls</c>; local content
/// (carbon://localhost) is unaffected. These drive the real per-window enforcement with the window
/// pointed at different URLs.
/// </summary>
public class RemoteCapabilityTests
{
    private static CarbonConfig Config(RemoteConfig? remote)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            Security = new SecurityConfig { Enabled = true },
        };
        config.Security.Capabilities["main"] = new CapabilityConfig
        {
            Windows = ["main"],
            Commands = ["test:ping"],
            Remote = remote,
        };
        return config;
    }

    private static (CapabilityManager caps, CarbonWindow main, CarbonApp app) Start(RemoteConfig? remote)
    {
        var app = CarbonApp.Create(Config(remote)).UsePlatform(new NoopHost());
        var handle = app.Start();
        return (handle.Services.GetRequiredService<CapabilityManager>(), handle.GetWindow("main"), app);
    }

    [Fact]
    public void Local_content_uses_the_capability_without_a_remote_entry()
    {
        var (caps, main, app) = Start(remote: null);
        try
        {
            main.Load("carbon://localhost/index.html");
            caps.EnsureCommandAllowed(main, "test:ping"); // local → allowed as before
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Remote_content_is_denied_without_a_remote_entry()
    {
        var (caps, main, app) = Start(remote: null);
        try
        {
            main.Load("https://app.example.com/");
            Assert.Throws<UnauthorizedAccessException>(() => caps.EnsureCommandAllowed(main, "test:ping"));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Remote_content_is_allowed_when_its_url_is_listed()
    {
        var (caps, main, app) = Start(new RemoteConfig { Urls = ["https://app.example.com"] });
        try
        {
            main.Load("https://app.example.com/");
            caps.EnsureCommandAllowed(main, "test:ping");
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Remote_url_glob_matches_subdomains_but_not_other_hosts()
    {
        var (caps, main, app) = Start(new RemoteConfig { Urls = ["https://*.example.com"] });
        try
        {
            main.Load("https://sub.example.com/");
            caps.EnsureCommandAllowed(main, "test:ping");

            main.Load("https://evil.com/");
            Assert.Throws<UnauthorizedAccessException>(() => caps.EnsureCommandAllowed(main, "test:ping"));
        }
        finally { app.Shutdown(); }
    }
}
