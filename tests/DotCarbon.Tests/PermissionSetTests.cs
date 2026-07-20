using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Security;
using DotCarbon.Plugins.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.4: every command gets an auto-generated fine-grained permission ("&lt;ns&gt;:allow-&lt;command&gt;"),
/// and a permission may compose other permissions (a set). These drive the real command check to prove
/// a single-command grant doesn't leak to siblings, and that composition resolves transitively.
/// </summary>
public class PermissionSetTests
{
    private static CarbonConfig Config(string permissionId)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            Security = new SecurityConfig { Enabled = true },
        };
        config.Security.Capabilities["main"] = new CapabilityConfig
        {
            Windows = ["main"],
            Permissions = [new PermissionEntry { Identifier = permissionId }],
        };
        return config;
    }

    [Fact]
    public void Auto_generated_permission_grants_only_its_own_command()
    {
        // fs is a generated plugin, so its command metadata drives the synthesized fs:allow-* permissions.
        var app = CarbonApp.Create(Config("fs:allow-read-file"))
            .UsePlatform(new NoopHost())
            .UsePlugin<FileSystemPlugin>();
        var handle = app.Start();
        try
        {
            var caps = handle.Services.GetRequiredService<CapabilityManager>();
            var main = handle.GetWindow("main");

            caps.EnsureCommandAllowed(main, "fs:read_file");
            Assert.Throws<UnauthorizedAccessException>(() => caps.EnsureCommandAllowed(main, "fs:write_file"));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Default_permission_still_grants_every_command()
    {
        var app = CarbonApp.Create(Config("fs:default"))
            .UsePlatform(new NoopHost())
            .UsePlugin<FileSystemPlugin>();
        var handle = app.Start();
        try
        {
            var caps = handle.Services.GetRequiredService<CapabilityManager>();
            var main = handle.GetWindow("main");

            caps.EnsureCommandAllowed(main, "fs:read_file");
            caps.EnsureCommandAllowed(main, "fs:write_file");
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Permission_set_composes_the_permissions_it_references()
    {
        var app = CarbonApp.Create(Config("t:read-write"))
            .UsePlatform(new NoopHost())
            .UsePlugin(new SetPlugin());
        var handle = app.Start();
        try
        {
            var caps = handle.Services.GetRequiredService<CapabilityManager>();
            var main = handle.GetWindow("main");

            // t:read-write → [t:allow-read, t:allow-write] → the two commands, but not t:erase.
            caps.EnsureCommandAllowed(main, "t:read");
            caps.EnsureCommandAllowed(main, "t:write");
            Assert.Throws<UnauthorizedAccessException>(() => caps.EnsureCommandAllowed(main, "t:erase"));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Composition_cycles_do_not_loop()
    {
        var app = CarbonApp.Create(Config("t:a"))
            .UsePlatform(new NoopHost())
            .UsePlugin(new CyclePlugin());
        var handle = app.Start();
        try
        {
            var caps = handle.Services.GetRequiredService<CapabilityManager>();
            var main = handle.GetWindow("main");

            // t:a → t:b → t:a; resolving a command it can't reach must terminate, not hang.
            Assert.Throws<UnauthorizedAccessException>(() => caps.EnsureCommandAllowed(main, "t:nope"));
        }
        finally { app.Shutdown(); }
    }

    // A permission set: t:read-write composes the auto-style allow permissions, which grant one command each.
    [CarbonPermission("t:allow-read", "Read.", Commands = ["t:read"])]
    [CarbonPermission("t:allow-write", "Write.", Commands = ["t:write"])]
    [CarbonPermission("t:read-write", "Read and write.", Commands = ["t:allow-read", "t:allow-write"])]
    private sealed class SetPlugin : IPlugin
    {
        public string Namespace => "t";
        public void Register(ICommandRegistry registry) { }
    }

    [CarbonPermission("t:a", "A.", Commands = ["t:b"])]
    [CarbonPermission("t:b", "B.", Commands = ["t:a"])]
    private sealed class CyclePlugin : IPlugin
    {
        public string Namespace => "t";
        public void Register(ICommandRegistry registry) { }
    }
}
