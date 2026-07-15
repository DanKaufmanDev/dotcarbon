using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

// Both generated mobile projects reference this backend to exercise the same command bridge.

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
        Console.WriteLine("[[CARBON_WEB_READY]]");
        Console.Out.Flush();
        return $"Hello, {req.Name}! (greet #{_state.GreetingCount})";
    }
}
