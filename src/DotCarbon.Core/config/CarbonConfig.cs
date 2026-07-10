namespace DotCarbon.Core.Config;

public class CarbonConfig
{
    public AppConfig App { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public BuildConfig Build { get; set; } = new();
    public BundleConfig Bundle { get; set; } = new();
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
    public string? BuildCommand { get; set; }
    public string DevUrl { get; set; } = "http://localhost:5173";
    public string FrontendDist { get; set; } = "../../ui/dist";
    public string? BackendProject { get; set; }
}

public class BundleConfig
{
    public string? Publisher { get; set; }
    public string? Copyright { get; set; }
    public string Category { get; set; } = "Utility";
    public List<string> Resources { get; set; } = [];
    public List<FileAssociationConfig> FileAssociations { get; set; } = [];
    public List<ProtocolConfig> Protocols { get; set; } = [];
    public MacOsBundleConfig MacOS { get; set; } = new();
    public WindowsBundleConfig Windows { get; set; } = new();
    public LinuxBundleConfig Linux { get; set; } = new();
    public UpdaterBundleConfig Updater { get; set; } = new();
}

public class FileAssociationConfig
{
    public string Name { get; set; } = "Document";
    public string Description { get; set; } = "";
    public List<string> Extensions { get; set; } = [];
    public string? MimeType { get; set; }
    public string Role { get; set; } = "Editor";
}

public class ProtocolConfig
{
    public string Name { get; set; } = "Application URL";
    public List<string> Schemes { get; set; } = [];
}

public class MacOsBundleConfig
{
    public string MinimumSystemVersion { get; set; } = "12.0";
    public string? SigningIdentity { get; set; }
    public string? Entitlements { get; set; }
    public string? NotarizationProfile { get; set; }
}

public class WindowsBundleConfig
{
    public string WebView2InstallMode { get; set; } = "downloadBootstrapper";
    public string? WebView2InstallerPath { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";
}

public class LinuxBundleConfig
{
    public string Category { get; set; } = "Utility";
}

public class UpdaterBundleConfig
{
    public bool Active { get; set; }
    public bool CreateArtifacts { get; set; }
    public List<string> Endpoints { get; set; } = [];
    public string? PublicKey { get; set; }
}
