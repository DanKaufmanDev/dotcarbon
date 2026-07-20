using System.Text.Json;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Security;
using DotCarbon.Plugins.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.3: a capability permission may carry per-capability allow/deny scopes. These are merged
/// across the capabilities applicable to a window and enforced by the owning plugin — here fs, which
/// treats the scope strings as path roots.
/// </summary>
public class PermissionScopeTests
{
    [Fact]
    public void Permission_parses_from_both_string_and_object_forms()
    {
        var json = """
        {
            "windows": ["main"],
            "permissions": [
                "fs:read",
                { "identifier": "fs:write", "allow": ["$APPDATA/*"], "deny": ["$APPDATA/secret"] }
            ]
        }
        """;

        var capability = JsonSerializer.Deserialize(json, CarbonConfigJsonContext.Default.CapabilityConfig)!;

        Assert.Equal(2, capability.Permissions.Count);
        Assert.Equal("fs:read", capability.Permissions[0].Identifier);
        Assert.Empty(capability.Permissions[0].Allow);

        Assert.Equal("fs:write", capability.Permissions[1].Identifier);
        Assert.Equal(["$APPDATA/*"], capability.Permissions[1].Allow);
        Assert.Equal(["$APPDATA/secret"], capability.Permissions[1].Deny);
    }

    private static CarbonConfig ConfigWith(params (string id, string[] allow, string[] deny)[] permissions)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            Security = new SecurityConfig { Enabled = true },
        };
        config.Security.Capabilities["main"] = new CapabilityConfig
        {
            Windows = ["main"],
            Permissions = [.. permissions.Select(p => new PermissionEntry
            {
                Identifier = p.id,
                Allow = [.. p.allow],
                Deny = [.. p.deny],
            })],
        };
        return config;
    }

    [Fact]
    public void Scopes_merge_across_capabilities_and_isolate_per_window()
    {
        var config = ConfigWith(("fs:default", ["$APPDATA/a"], ["$APPDATA/a/secret"]));
        // A second capability, also on main, adds another allow root.
        config.Security.Capabilities["extra"] = new CapabilityConfig
        {
            Windows = ["main"],
            Permissions = [new PermissionEntry { Identifier = "fs:default", Allow = ["$DOCUMENTS/b"] }],
        };

        var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var caps = handle.Services.GetRequiredService<CapabilityManager>();

            var main = caps.ResolveScope(handle.GetWindow("main"), "fs");
            Assert.Equal(["$APPDATA/a", "$DOCUMENTS/b"], main.Allow.OrderBy(s => s).ToArray());
            Assert.Equal(["$APPDATA/a/secret"], main.Deny);

            // A window the capabilities don't target carries no scopes.
            var second = caps.ResolveScope(handle.CreateWindow("second"), "fs");
            Assert.Empty(second.Allow);
            Assert.Empty(second.Deny);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Fs_plugin_enforces_capability_allow_and_deny()
    {
        var allowDir = Directory.CreateTempSubdirectory("carbon-fs-allow-").FullName;
        var outsideDir = Directory.CreateTempSubdirectory("carbon-fs-outside-").FullName;
        var secretDir = Path.Combine(allowDir, "secret");
        Directory.CreateDirectory(secretDir);
        try
        {
            var config = ConfigWith(("fs:default", [allowDir], [secretDir]));
            var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
            var handle = app.Start();
            try
            {
                var fs = new FileSystemPlugin(handle);

                // Inside the granted allow scope → permitted.
                var okPath = Path.Combine(allowDir, "note.txt");
                await fs.WriteFile(new WriteFileArgs(okPath, "hi"));
                Assert.Equal("hi", await fs.ReadFile(new ReadFileArgs(okPath)));

                // Outside every allow scope → denied.
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => fs.ReadFile(new ReadFileArgs(Path.Combine(outsideDir, "x.txt"))));

                // Inside a deny scope (even though it's under the allow root) → denied.
                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => fs.WriteFile(new WriteFileArgs(Path.Combine(secretDir, "x.txt"), "no")));
            }
            finally { app.Shutdown(); }
        }
        finally
        {
            Directory.Delete(allowDir, recursive: true);
            Directory.Delete(outsideDir, recursive: true);
        }
    }
}
