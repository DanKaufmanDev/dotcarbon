using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

// Shared backend for the mobile smoke app — the same [CarbonCommand] pattern desktop uses.
// The generated .carbon/platforms/{android,ios} projects reference this library.

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
        return $"Hello, {req.Name}! (greet #{_state.GreetingCount})";
    }
}
