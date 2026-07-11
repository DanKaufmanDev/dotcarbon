using DotCarbon.Core.Bridge;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Plugins;

public interface IPlugin
{
    string Namespace { get; }

    PluginMetadata Metadata => PluginMetadata.FromPlugin(this);

    void Register(ICommandRegistry registry);

    ValueTask InitializeAsync(PluginContext context) => ValueTask.CompletedTask;

    ValueTask OnLifecycleAsync(CarbonLifecycleEvent lifecycleEvent) => ValueTask.CompletedTask;

    ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
