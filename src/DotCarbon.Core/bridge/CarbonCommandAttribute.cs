namespace DotCarbon.Core.Bridge;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CarbonCommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
