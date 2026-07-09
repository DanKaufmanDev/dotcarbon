using DotCarbon.Core.Bridge;

namespace DotCarbon.Core.Plugins;

public interface IPlugin
{
    string Namespace { get; }

    void Register(ICommandRegistry registry);
}
