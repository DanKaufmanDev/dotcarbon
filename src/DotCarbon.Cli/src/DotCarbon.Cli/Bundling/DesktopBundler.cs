using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Plans and packages a Carbon desktop app for macOS, Windows, or Linux.
/// </summary>
internal sealed class DesktopBundler : IBundlerTarget
{
    public string Id => "desktop";
    public string DisplayName => "Desktop";

    public bool IsSupported(BundleContext context, out string? reason)
    {
        reason = null;
        return true;
    }

    public BundlePlan Plan(BundleContext ctx)
    {
        var os = OsFamily(ctx.Target);
        var steps = new List<BundleStep>
        {
            new("Validate configuration",
                "app identity, versioning, icons, and bundle/updater requirements"),
            new("Prepare icons",
                "generate platform icons from the source image (or Carbon defaults)"),
            new("Build frontend", FrontendStep(ctx)),
            new("Publish .NET host",
                $"{ctx.Target}, {(ctx.Aot ? "NativeAOT" : "single-file self-contained")}, frontend embedded in the binary"),
        };

        if (ctx.Package)
            steps.Add(new("Package installer", PackageFormat(ctx, os)));
        else
            steps.Add(new("Package installer", PackageFormat(ctx, os), Skipped: true, SkipReason: "--no-package"));

        if (os is "osx" or "win")
        {
            var (signs, why) = SignState(ctx, os);
            steps.Add(signs
                ? new(SignLabel(os), "sign the packaged app so it launches without security warnings")
                : new(SignLabel(os), "sign the packaged app", Skipped: true, SkipReason: why));
        }

        if (os == "osx")
        {
            var notarize = ctx.Package && HasMacNotarization(ctx.Config);
            steps.Add(notarize
                ? new("Notarize (Apple)", "submit the .dmg to notarytool and staple the ticket")
                : new("Notarize (Apple)", "submit to Apple notary service", Skipped: true,
                    SkipReason: !ctx.Package ? "--no-package" : "no notarization profile configured"));
        }

        var wantUpdater = ctx.UpdaterArtifacts || ctx.Config.Bundle.Updater.CreateArtifacts;
        steps.Add(wantUpdater
            ? new("Sign updater artifacts", "produce signed updater metadata (latest.json + signature)")
            : new("Sign updater artifacts", "produce signed updater metadata", Skipped: true,
                SkipReason: "updater artifacts not requested"));

        steps.Add(new("Write build manifest", $"out/{ctx.Target}/ manifest with SHA-256 of every artifact"));

        return new BundlePlan
        {
            TargetId = Id,
            TargetName = $"{DisplayName} ({ctx.Target})",
            Summary = ctx.Package
                ? $"one Carbon app → {PackageFormat(ctx, os)}"
                : "one Carbon app → self-contained executable (no installer)",
            Steps = steps,
        };
    }

    public async Task<int> ExecuteAsync(BundleContext ctx)
    {
        Plan(ctx).Render(dryRun: false);
        return await BuildCommand.Run(
            ctx.ProjectDir, ctx.Target, ctx.Aot, ctx.Package, ctx.UpdaterArtifacts, ctx.Debug, ctx.Formats);
    }

    /// <summary>
    /// What the frontend step will actually do. A project with a prebuilt, committed dist and no build
    /// command skips the build entirely, so the plan must say that rather than name a command that
    /// will never run.
    /// </summary>
    private static string FrontendStep(BundleContext ctx)
    {
        var dist = ctx.Config.Build.FrontendDist;
        if (string.IsNullOrWhiteSpace(ctx.Config.Build.BuildCommand) &&
            File.Exists(Path.Combine(ctx.WorkingDir, dist, "index.html")))
        {
            return $"skipped — {dist} is already built";
        }

        var command = string.IsNullOrWhiteSpace(ctx.Config.Build.BuildCommand)
            ? ctx.Config.Build.DevCommand.Replace("run dev", "run build").Replace(" dev", " build")
            : ctx.Config.Build.BuildCommand;
        return $"{command} → {dist}";
    }

    private static string OsFamily(string target) =>
        target.StartsWith("osx") ? "osx" :
        target.StartsWith("win") ? "win" :
        target.StartsWith("linux") ? "linux" : "unknown";

    private static string PackageFormat(BundleContext ctx, string os) => os switch
    {
        "osx" => ".app + .dmg",
        "win" => ".msi (+ .exe)",
        "linux" => LinuxFormats(ctx),
        _ => "platform installer",
    };

    private static string LinuxFormats(BundleContext ctx)
    {
        var formats = ctx.Config.Bundle.Linux.Formats
            .Select(format => format.Trim().ToLowerInvariant())
            .Where(format => format is "appimage" or "deb" or "rpm" or "flatpak" or "snap")
            .Distinct()
            .Select(format => format switch
            {
                "appimage" => ".AppImage",
                "flatpak" => "flatpak manifest",
                "snap" => "snapcraft.yaml",
                _ => "." + format,
            })
            .ToList();
        return formats.Count > 0 ? string.Join(" + ", formats) : ".AppImage";
    }

    private static string SignLabel(string os) => os switch
    {
        "osx" => "Code sign (codesign)",
        "win" => "Code sign (signtool)",
        _ => "Code sign",
    };

    private static (bool signs, string reason) SignState(BundleContext ctx, string os)
    {
        if (!ctx.Package) return (false, "--no-package");
        return os switch
        {
            "osx" => HasMacSigning(ctx.Config)
                ? (true, "")
                : (false, "no macOS signing identity configured"),
            "win" => !string.IsNullOrWhiteSpace(ctx.Config.Bundle.Windows.CertificateThumbprint)
                ? (true, "")
                : (false, "no Windows certificate thumbprint configured"),
            _ => (false, "unsupported"),
        };
    }

    private static bool HasMacSigning(CarbonConfig config) =>
        !string.IsNullOrWhiteSpace(config.Bundle.MacOS.SigningIdentity)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPLE_SIGNING_IDENTITY"));

    private static bool HasMacNotarization(CarbonConfig config) =>
        !string.IsNullOrWhiteSpace(config.Bundle.MacOS.NotarizationProfile)
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPLE_NOTARIZATION_PROFILE"));
}
