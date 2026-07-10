using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Plugins;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .Manage(new AppState())
    .WithPlugin<AppCommands>()
    .Run();

public record GreetRequest(string Name);

public sealed class AppState
{
    public int GreetingCount { get; set; }
}

public partial class AppCommands : IPlugin
{
    private readonly AppState _state;

    public AppCommands(AppState state)
    {
        _state = state;
    }

    public string Namespace => "app";

    [CarbonCommand("greet")]
    public string Greet(GreetRequest req)
    {
        _state.GreetingCount++;
        return $"Hello, {req.Name}! You've been greeted {_state.GreetingCount} time(s) from C# ⚡";
    }
}
