using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Host.Desktop;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 2.10: the tray/menu command surface the frontend invokes.
///
/// These deliberately execute the handlers rather than only checking the names. The command
/// generator binds every command in an assembly to a single JsonSerializerContext, so an argument
/// type missing from it builds cleanly and only fails when the command is actually invoked — which
/// is exactly the shape of bug this catches. Executing is safe off-screen: the native setters
/// address a tray/menu that does not exist here and ignore unknown state.
/// </summary>
public class DesktopUiPluginTests
{
    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<JsonNode?>>> Handlers { get; } = new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler) => Handlers[name] = handler;
    }

    private static FakeRegistry Register(DotCarbon.Core.Plugins.IPlugin plugin)
    {
        var registry = new FakeRegistry();
        plugin.Register(registry);
        return registry;
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Tray_plugin_registers_its_commands()
    {
        var registry = Register(new TrayPlugin());

        Assert.Equal(
            new[]
            {
                "tray:remove", "tray:set_icon", "tray:set_menu", "tray:set_title", "tray:set_tooltip",
                "tray:set_visible",
            },
            registry.Handlers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Menu_plugin_registers_its_commands()
    {
        var registry = Register(new MenuPlugin());

        Assert.Equal(
            new[] { "menu:set_app_menu", "menu:set_checked", "menu:set_enabled", "menu:set_label" },
            registry.Handlers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("tray:set_icon", """{"path":"/tmp/icon.png","isTemplate":true}""")]
    [InlineData("tray:set_icon", """{"path":"/tmp/icon.png"}""")] // isTemplate is optional
    [InlineData("tray:set_title", """{"title":"hi"}""")]
    [InlineData("tray:set_tooltip", """{"tooltip":"hi"}""")]
    [InlineData("tray:set_visible", """{"visible":false}""")]
    [InlineData("tray:remove", "{}")]
    public async Task Tray_commands_accept_their_arguments(string command, string json)
    {
        var registry = Register(new TrayPlugin());

        // Throws if the argument type is missing from the assembly's serializer context.
        Assert.Null(await registry.Handlers[command](Payload(json)));
    }

    [Theory]
    [InlineData("menu:set_enabled", """{"id":"about","enabled":false}""")]
    [InlineData("menu:set_checked", """{"id":"verbose","checked":true}""")]
    [InlineData("menu:set_label", """{"id":"about","label":"About"}""")]
    public async Task Menu_commands_accept_their_arguments(string command, string json)
    {
        var registry = Register(new MenuPlugin());

        Assert.Null(await registry.Handlers[command](Payload(json)));
    }

    // Task 2.11. The declared-menu specs are recursive and much richer than the flat argument
    // records, so they are the likeliest thing to be missing from the serializer context or to break
    // on a nested shape. Rebuilding is a no-op without a running app, which is what we want here:
    // the point is that the argument survives the bridge, not that a native menu appears.
    [Fact]
    public async Task Set_app_menu_accepts_a_nested_declared_menu()
    {
        var registry = Register(new MenuPlugin());

        var json = """
        {"menus":[{"label":"File","items":[
            {"id":"new","label":"New","shortcut":"CmdOrCtrl+N"},
            {"id":"chk","label":"Check","checked":true},
            {"separator":true},
            {"label":"Recent","items":[{"id":"deep","label":"Deep","items":[{"id":"deeper","label":"Deeper"}]}]},
            {"role":"Quit"}
        ]}]}
        """;

        Assert.Null(await registry.Handlers["menu:set_app_menu"](Payload(json)));
    }

    [Fact]
    public async Task Set_tray_menu_accepts_a_nested_declared_menu()
    {
        var registry = Register(new TrayPlugin());

        var json = """
        {"items":[
            {"id":"show","label":"Show"},
            {"separator":true},
            {"label":"More","items":[{"id":"nested","label":"Nested"}]}
        ]}
        """;

        Assert.Null(await registry.Handlers["tray:set_menu"](Payload(json)));
    }

    [Fact]
    public async Task Set_app_menu_accepts_an_empty_menu()
    {
        var registry = Register(new MenuPlugin());

        // Clearing the menu is a legitimate request, not a malformed one.
        Assert.Null(await registry.Handlers["menu:set_app_menu"](Payload("""{"menus":[]}""")));
    }

    [Fact]
    public void Tray_plugin_declares_a_wildcard_permission()
    {
        var metadata = new TrayPlugin().Metadata;

        Assert.Equal("tray", metadata.Namespace);
        Assert.Contains(metadata.Permissions, p => p.Commands.Contains("tray:*"));
    }
}
