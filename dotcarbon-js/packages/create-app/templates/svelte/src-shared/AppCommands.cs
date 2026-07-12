using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

// Shared backend. These [CarbonCommand] classes run on desktop AND mobile —
// src-carbon (desktop) and the generated .carbon/platforms/* projects both reference this library.

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
