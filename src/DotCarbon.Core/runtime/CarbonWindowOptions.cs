using DotCarbon.Core.Config;

namespace DotCarbon.Core.Runtime;

public sealed class CarbonWindowOptions
{
    public string Label { get; set; } = "main";
    public string? Url { get; set; }
    public string? ParentLabel { get; set; }
    public List<string> Capabilities { get; set; } = [];
    public string Title { get; set; } = "Carbon App";
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public int? MinWidth { get; set; }
    public int? MinHeight { get; set; }
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Center { get; set; } = true;
    public bool Resizable { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool Maximized { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool Decorations { get; set; } = true;
    public bool Transparent { get; set; }
    public bool DevTools { get; set; } = true;
    public bool ContextMenu { get; set; } = true;
    public string? Icon { get; set; }

    internal static CarbonWindowOptions FromConfig(WindowConfig config) => new()
    {
        Label = config.Label,
        Url = config.Url,
        ParentLabel = config.Parent,
        Capabilities = [.. config.Capabilities],
        Title = config.Title,
        Width = config.Width,
        Height = config.Height,
        MinWidth = config.MinWidth,
        MinHeight = config.MinHeight,
        MaxWidth = config.MaxWidth,
        MaxHeight = config.MaxHeight,
        X = config.X,
        Y = config.Y,
        Center = config.Center,
        Resizable = config.Resizable,
        Fullscreen = config.Fullscreen,
        Maximized = config.Maximized,
        AlwaysOnTop = config.AlwaysOnTop,
        Decorations = config.Decorations,
        Transparent = config.Transparent,
        DevTools = config.DevTools,
        ContextMenu = config.ContextMenu,
        Icon = config.Icon,
    };
}
