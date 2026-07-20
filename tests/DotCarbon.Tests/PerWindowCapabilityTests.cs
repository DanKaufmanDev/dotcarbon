using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.1: a capability's <c>windows</c> list scopes its permissions to specific windows instead of
/// granting them app-wide. These drive the real enforcement path (per invoking window) to prove one
/// window can't use a capability targeted at another, and that glob patterns cover families of windows.
/// </summary>
public class PerWindowCapabilityTests
{
    private static CarbonConfig Config(string capabilityId, params string[] windows)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            Security = new SecurityConfig { Enabled = true },
        };
        config.Security.Capabilities[capabilityId] =
            new CapabilityConfig { Windows = [.. windows], Commands = ["test:ping"] };
        return config;
    }

    private static (CapabilityManager caps, AppHandle handle, CarbonApp app) Start(CarbonConfig config)
    {
        var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
        var handle = app.Start();
        return (handle.Services.GetRequiredService<CapabilityManager>(), handle, app);
    }

    [Fact]
    public void Capability_reaches_only_its_targeted_window()
    {
        var (caps, handle, app) = Start(Config("main-only", "main"));
        try
        {
            var second = handle.CreateWindow("second");

            caps.EnsureCommandAllowed(handle.GetWindow("main"), "test:ping"); // targeted → allowed
            Assert.Throws<UnauthorizedAccessException>(
                () => caps.EnsureCommandAllowed(second, "test:ping"));        // not targeted → denied
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Glob_window_pattern_covers_a_family_of_windows()
    {
        var (caps, handle, app) = Start(Config("editors", "editor-*"));
        try
        {
            var editor1 = handle.CreateWindow("editor-1");
            var editor2 = handle.CreateWindow("editor-2");

            caps.EnsureCommandAllowed(editor1, "test:ping");
            caps.EnsureCommandAllowed(editor2, "test:ping");
            // The main window is not an editor-*, so the same capability does not reach it.
            Assert.Throws<UnauthorizedAccessException>(
                () => caps.EnsureCommandAllowed(handle.GetWindow("main"), "test:ping"));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Star_targets_every_window()
    {
        var (caps, handle, app) = Start(Config("everywhere", "*"));
        try
        {
            var second = handle.CreateWindow("second");

            caps.EnsureCommandAllowed(handle.GetWindow("main"), "test:ping");
            caps.EnsureCommandAllowed(second, "test:ping");
        }
        finally { app.Shutdown(); }
    }
}
