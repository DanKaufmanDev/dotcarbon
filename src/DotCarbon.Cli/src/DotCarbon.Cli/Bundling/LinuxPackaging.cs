using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Generates Flatpak and Snap packaging manifests from a Carbon config. Both are opt-in Linux formats
/// (<c>bundle.linux.formats</c>). The generators are pure so their content — app id, launch command,
/// and the sandbox permissions a WebKitGTK app actually needs — is unit-testable; running
/// <c>flatpak-builder</c>/<c>snapcraft</c> is the user's step on a Linux box with those tools, so the
/// bundler writes the manifest (and, when the tool is present, invokes it).
/// </summary>
internal static class LinuxPackaging
{
    /// <summary>The freedesktop runtime the Flatpak manifest targets.</summary>
    private const string FlatpakRuntimeVersion = "24.08";

    /// <summary>
    /// A Flatpak manifest (YAML). The finish-args are the ones a WebKitGTK webview app needs to run
    /// sandboxed: a GPU device for rendering, Wayland with an X11 fallback, audio, and network.
    /// </summary>
    public static string FlatpakManifest(CarbonConfig config, string slug, string exeName)
    {
        var appId = config.App.Identifier;
        return
            $"app-id: {appId}\n" +
            "runtime: org.freedesktop.Platform\n" +
            $"runtime-version: '{FlatpakRuntimeVersion}'\n" +
            "sdk: org.freedesktop.Sdk\n" +
            $"command: {slug}\n" +
            "finish-args:\n" +
            "  - --share=ipc\n" +
            "  - --socket=wayland\n" +
            "  - --socket=fallback-x11\n" +
            "  - --device=dri\n" +
            "  - --socket=pulseaudio\n" +
            "  - --share=network\n" +
            "  - --talk-name=org.freedesktop.Notifications\n" +
            "modules:\n" +
            $"  - name: {slug}\n" +
            "    buildsystem: simple\n" +
            "    build-commands:\n" +
            // Install the whole published payload under /app/lib and expose a launcher on the path.
            $"      - mkdir -p /app/lib/{slug}\n" +
            $"      - cp -r payload/* /app/lib/{slug}/\n" +
            $"      - install -Dm755 launcher /app/bin/{slug}\n" +
            $"      - install -Dm644 {slug}.desktop /app/share/applications/{appId}.desktop\n" +
            "    sources:\n" +
            "      - type: dir\n" +
            "        path: .\n";
    }

    /// <summary>The <c>launcher</c> script the Flatpak manifest installs to <c>/app/bin/&lt;slug&gt;</c>.</summary>
    public static string FlatpakLauncher(string slug, string exeName) =>
        "#!/bin/sh\n" +
        $"export CARBON_RESOURCE_DIR=\"/app/lib/{slug}\"\n" +
        $"exec \"/app/lib/{slug}/{exeName}\" \"$@\"\n";

    /// <summary>
    /// A <c>snapcraft.yaml</c>. Strict confinement with the plugs a webview app needs; the prebuilt
    /// payload is copied in with the <c>dump</c> plugin rather than rebuilt.
    /// </summary>
    public static string SnapcraftYaml(CarbonConfig config, string slug, string exeName)
    {
        var summary = Truncate(string.IsNullOrWhiteSpace(config.App.Name) ? slug : config.App.Name, 78);
        var description = string.IsNullOrWhiteSpace(config.Bundle.Publisher)
            ? config.App.Name
            : $"{config.App.Name} by {config.Bundle.Publisher}";

        return
            $"name: {slug}\n" +
            $"version: '{config.App.Version}'\n" +
            $"summary: {summary}\n" +
            $"description: {description}\n" +
            "confinement: strict\n" +
            "base: core24\n" +
            "grade: stable\n" +
            "apps:\n" +
            $"  {slug}:\n" +
            $"    command: bin/{slug}\n" +
            "    extensions: [gnome]\n" +
            "    plugs:\n" +
            "      - home\n" +
            "      - network\n" +
            "      - network-bind\n" +
            "      - opengl\n" +
            "      - audio-playback\n" +
            "      - browser-support\n" +
            "      - desktop\n" +
            "      - desktop-legacy\n" +
            "      - wayland\n" +
            "      - x11\n" +
            "parts:\n" +
            $"  {slug}:\n" +
            "    plugin: dump\n" +
            "    source: payload/\n" +
            "    organize:\n" +
            $"      '*': lib/{slug}/\n" +
            "    override-prime: |\n" +
            "      craftctl default\n" +
            $"      mkdir -p bin\n" +
            $"      printf '#!/bin/sh\\nexport CARBON_RESOURCE_DIR=\"$SNAP/lib/{slug}\"\\nexec \"$SNAP/lib/{slug}/{exeName}\" \"$@\"\\n' > bin/{slug}\n" +
            $"      chmod +x bin/{slug}\n";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
