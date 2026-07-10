using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;

var config = ConfigLoader.Load();

new CarbonHost(config)
    .WithPlugin(new AppCommands())
    .Run();

public record GreetRequest(string Name);

public partial class AppCommands : IPlugin
{
    public string Namespace => "app";

    [CarbonCommand("greet")]
    public string Greet(GreetRequest req) =>
        $"Hello, {req.Name}! You've been greeted from C# ⚡";
}
