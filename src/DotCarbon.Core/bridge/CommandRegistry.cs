using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Bridge;

public interface ICommandRegistry
{
    void Add(string name, Func<JsonElement, Task<JsonNode?>> handler);
}

public class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, Func<JsonElement, Task<JsonNode?>>> _handlers = new();

    public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler)
    {
        Console.WriteLine($"[Carbon] Registered command: {name}");
        _handlers[name] = handler;
    }

    public void RegisterPlugin(IPlugin plugin) => plugin.Register(this);

    public bool HasCommand(string name) => _handlers.ContainsKey(name);

    public async Task<JsonNode?> InvokeAsync(
        string name,
        JsonElement payload,
        CarbonCommandContext? context = null)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            throw new KeyNotFoundException($"No command registered: {name}");

        using var scope = context?.App.EnterInvocation(context);
        return await handler(payload);
    }
}

public sealed record CarbonCommandContext(AppHandle App, CarbonWindow Window);
