namespace DotCarbon.Core.Config;

public class CarbonConfig
{
    public AppConfig App { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public BuildConfig Build { get; set; } = new();
}

public class AppConfig
{
    public string Name { get; set; } = "Carbon App";

    public string Version { get; set; } = "0.1.0";

    public string Identifier { get; set; } = "com.example.app";
}

public class WindowConfig
{
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
    public bool Fullscreen { get; set; } = false;
    public bool Maximized { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;

    public bool Decorations { get; set; } = true;

    public bool Transparent { get; set; } = false;

    public bool DevTools { get; set; } = true;

    public bool ContextMenu { get; set; } = true;

    public string? Icon { get; set; }
}

public class BuildConfig
{
    public string DevCommand { get; set; } = "pnpm dev";
    public string DevUrl { get; set; } = "http://localhost:5173";
    public string FrontendDist { get; set; } = "../../ui/dist";
}
