using System.Text.Json;

namespace DotCarbon.Core.Config;

public class CarbonConfig
{
    public AppConfig App { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public List<WindowConfig> Windows { get; set; } = [];
    public SecurityConfig Security { get; set; } = new();
    public Dictionary<string, JsonElement> Plugins { get; set; } = [];
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
    public string Label { get; set; } = "main";
    public string? Url { get; set; }
    public string? Parent { get; set; }
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
    public bool Fullscreen { get; set; } = false;
    public bool Maximized { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;

    public bool Decorations { get; set; } = true;

    public bool Transparent { get; set; } = false;

    public bool DevTools { get; set; } = true;

    public bool ContextMenu { get; set; } = true;

    public string? Icon { get; set; }
}

public class SecurityConfig
{
    public bool Enabled { get; set; } = true;
    public bool DevAllowAll { get; set; }
    public bool AllowExternalUrls { get; set; }
    public bool AllowSourceMaps { get; set; }
    public int MaxBridgeMessageBytes { get; set; } = 1024 * 1024;
    public int MaxEventPayloadBytes { get; set; } = 256 * 1024;
    public string? ContentSecurityPolicy { get; set; } =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "media-src 'self' blob:; " +
        "worker-src 'self' blob:; " +
        "object-src 'none'; " +
        "base-uri 'none'; " +
        "frame-ancestors 'none'";
    public List<string> AllowedOrigins { get; set; } = [];
    public List<string> DefaultCapabilities { get; set; } = [];
    public Dictionary<string, CapabilityConfig> Capabilities { get; set; } = [];
}

public class CapabilityConfig
{
    public string? Identifier { get; set; }
    public string? Description { get; set; }
    public List<string> Windows { get; set; } = [];
    public List<string> Commands { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
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
    public AndroidBundleConfig Android { get; set; } = new();
    public IosBundleConfig Ios { get; set; } = new();
    public UpdaterBundleConfig Updater { get; set; } = new();
}

public class AndroidBundleConfig
{
    /// <summary>Android application id. Defaults to app.identifier.</summary>
    public string? Package { get; set; }
    public int MinSdk { get; set; } = 24;
    public int TargetSdk { get; set; } = 34;
    public int CompileSdk { get; set; } = 34;
}

public class IosBundleConfig
{
    /// <summary>iOS bundle identifier. Defaults to app.identifier.</summary>
    public string? BundleIdentifier { get; set; }
    public string MinimumOSVersion { get; set; } = "15.0";
    public string? DevelopmentTeam { get; set; }
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

    /// <summary>Which Linux packages to produce. Any of: appimage, deb, rpm.</summary>
    public List<string> Formats { get; set; } = ["appimage", "deb", "rpm"];

    /// <summary>deb/rpm packager identity, e.g. "Jane Doe &lt;jane@example.com&gt;". Falls back to bundle.publisher/app.name.</summary>
    public string? Maintainer { get; set; }

    /// <summary>Debian archive section (control "Section").</summary>
    public string Section { get; set; } = "utils";

    /// <summary>Debian priority (control "Priority").</summary>
    public string Priority { get; set; } = "optional";

    /// <summary>Runtime package dependencies (deb "Depends" / rpm "Requires"), e.g. "libwebkit2gtk-4.1-0".</summary>
    public List<string> Depends { get; set; } = [];

    /// <summary>rpm license tag.</summary>
    public string License { get; set; } = "Proprietary";
}

public class UpdaterBundleConfig
{
    public bool Active { get; set; }
    public bool CreateArtifacts { get; set; }
    public List<string> Endpoints { get; set; } = [];
    public string? PublicKey { get; set; }
}
