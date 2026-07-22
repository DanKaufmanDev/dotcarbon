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
    public PermissionsConfig Permissions { get; set; } = new();
}

/// <summary>
/// Device capabilities the app needs. Mapped to native declarations per platform:
/// AndroidManifest <c>&lt;uses-permission&gt;</c>, iOS Info.plist usage-description keys, and entitlements.
/// </summary>
public class PermissionsConfig
{
    public bool Camera { get; set; }
    public bool Microphone { get; set; }
    public bool Location { get; set; }
    public bool Notifications { get; set; }
    public bool Contacts { get; set; }
    public bool PhotoLibrary { get; set; }

    /// <summary>Haptic feedback (Android's VIBRATE; a normal permission, so no runtime prompt).</summary>
    public bool Vibrate { get; set; }

    /// <summary>File access scope: null (none), "appData", "documents", or "external".</summary>
    public string? Files { get; set; }

    /// <summary>iOS Info.plist usage strings per permission id (e.g. "camera"). Defaults are used if unset.</summary>
    public Dictionary<string, string> Descriptions { get; set; } = [];
}

public class AppConfig
{
    public string Name { get; set; } = "Carbon App";

    /// <summary>User-facing app name shown on the home screen / launcher. Defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; set; }

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

    /// <summary>
    /// How the title bar is drawn. "visible" (default) is a normal OS title bar with the content
    /// below it. "transparent" makes the webview fill the whole window with the traffic-light controls
    /// floating over it and the title hidden — a full-window app. macOS only for now; elsewhere it
    /// falls back to a normal title bar. Distinct from <see cref="Decorations"/> = false, which removes
    /// the frame and controls entirely.
    /// </summary>
    public string TitleBarStyle { get; set; } = "visible";

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

    /// <summary>
    /// Directory roots that <c>convertFileSrc</c> may serve local files from over <c>carbon://</c>
    /// (Task 4.6). Accepts <c>$appdata</c>/<c>$documents</c>/<c>$downloads</c>/<c>$home</c>/<c>$temp</c>
    /// shortcuts, <c>~</c> expansion, and absolute paths. Empty (the default) denies everything.
    /// </summary>
    public string[]? AssetScope { get; set; }
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
    public List<PermissionEntry> Permissions { get; set; } = [];

    /// <summary>
    /// Remote origins allowed to use this capability's permissions. Without it, a capability applies
    /// only to local content (<c>carbon://localhost</c> and your dev server); remote content is denied
    /// the bridge by default. Each URL supports <c>*</c>/<c>?</c> globs, e.g. <c>https://*.example.com</c>.
    /// </summary>
    public RemoteConfig? Remote { get; set; }
}

public class RemoteConfig
{
    public List<string> Urls { get; set; } = [];
}

/// <summary>
/// A permission granted by a capability. In JSON it may be a bare string (just the identifier) or an
/// object carrying per-capability scopes: <c>{ "identifier": "fs:default", "allow": [...], "deny": [...] }</c>.
/// The <c>allow</c>/<c>deny</c> entries are opaque strings interpreted by the owning plugin (paths for
/// fs, URLs for http, …) and merged across every capability that grants the permission to a window.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(PermissionEntryConverter))]
public sealed class PermissionEntry
{
    public string? Identifier { get; set; }
    public List<string> Allow { get; set; } = [];
    public List<string> Deny { get; set; } = [];
}

/// <summary>Reads a <see cref="PermissionEntry"/> from either a string or an object (AOT-safe, manual).</summary>
public sealed class PermissionEntryConverter : System.Text.Json.Serialization.JsonConverter<PermissionEntry>
{
    public override PermissionEntry Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            return new PermissionEntry { Identifier = reader.GetString() };

        if (reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            throw new System.Text.Json.JsonException("A capability permission must be a string or an object.");

        var entry = new PermissionEntry();
        while (reader.Read())
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject) return entry;
            if (reader.TokenType != System.Text.Json.JsonTokenType.PropertyName) continue;

            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "identifier": entry.Identifier = reader.GetString(); break;
                case "allow": entry.Allow = ReadStringArray(ref reader); break;
                case "deny": entry.Deny = ReadStringArray(ref reader); break;
                default: reader.Skip(); break;
            }
        }
        throw new System.Text.Json.JsonException("Unterminated permission object.");
    }

    private static List<string> ReadStringArray(ref System.Text.Json.Utf8JsonReader reader)
    {
        var items = new List<string>();
        if (reader.TokenType != System.Text.Json.JsonTokenType.StartArray) return items;
        while (reader.Read() && reader.TokenType != System.Text.Json.JsonTokenType.EndArray)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                var value = reader.GetString();
                if (!string.IsNullOrEmpty(value)) items.Add(value);
            }
        }
        return items;
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        PermissionEntry value,
        System.Text.Json.JsonSerializerOptions options)
    {
        // Round-trips to the object form; the string form is only an input convenience.
        writer.WriteStartObject();
        writer.WriteString("identifier", value.Identifier);
        writer.WritePropertyName("allow");
        WriteStringArray(writer, value.Allow);
        writer.WritePropertyName("deny");
        WriteStringArray(writer, value.Deny);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(System.Text.Json.Utf8JsonWriter writer, List<string> items)
    {
        writer.WriteStartArray();
        foreach (var item in items) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
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
    /// <summary>
    /// Platforms or concrete bundle targets this app targets: "desktop", "android", "ios",
    /// or desktop RIDs such as "osx-arm64" / "win-x64". Defaults to desktop.
    /// </summary>
    public List<string> Targets { get; set; } = ["desktop"];
    public string? Publisher { get; set; }
    public string? Copyright { get; set; }
    public string Category { get; set; } = "Utility";
    public List<string> Resources { get; set; } = [];

    /// <summary>
    /// External executables ("sidecars") bundled next to the app binary. Each entry is a path prefix
    /// (e.g. "binaries/my-tool"); the bundler picks the variant matching the build's target triple
    /// ("binaries/my-tool-aarch64-apple-darwin") and copies it beside the executable, triple dropped.
    /// Run them at runtime with the shell plugin's <c>sidecar()</c>.
    /// </summary>
    public List<string> ExternalBin { get; set; } = [];
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
    public AndroidSigningConfig Signing { get; set; } = new();
}

/// <summary>
/// Android release signing. Passwords are never stored here — they come from the environment:
/// <c>CARBON_ANDROID_KEYSTORE_PASSWORD</c> and <c>CARBON_ANDROID_KEY_PASSWORD</c>.
/// </summary>
public class AndroidSigningConfig
{
    /// <summary>Path to the keystore (.jks/.keystore), relative to the project root. Enables release signing when set.</summary>
    public string? Keystore { get; set; }
    public string? KeyAlias { get; set; }
}

public class IosBundleConfig
{
    /// <summary>iOS bundle identifier. Defaults to app.identifier.</summary>
    public string? BundleIdentifier { get; set; }
    public string MinimumOSVersion { get; set; } = "15.0";
    public string? DevelopmentTeam { get; set; }
    public IosSigningConfig Signing { get; set; } = new();
}

/// <summary>
/// iOS device/archive signing. The certificate and provisioning profile are installed into the
/// build machine's keychain (locally, or from CI secrets); only their names live here.
/// </summary>
public class IosSigningConfig
{
    /// <summary>Codesign identity, e.g. "Apple Distribution: Example (TEAMID)".</summary>
    public string? Identity { get; set; }
    /// <summary>Provisioning profile name or UUID.</summary>
    public string? ProvisioningProfile { get; set; }
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
