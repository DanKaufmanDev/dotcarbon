using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Packages a Carbon app for desktop (macOS, Windows, Linux). This is the normalized
/// front door for the existing production build: it describes the pipeline as an
/// ordered plan, then drives the same battle-tested build engine.
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
            new("Build frontend",
                $"{(string.IsNullOrWhiteSpace(ctx.Config.Build.BuildCommand) ? "vite build" : ctx.Config.Build.BuildCommand)} → {ctx.Config.Build.FrontendDist}"),
            new("Publish .NET host",
                $"{ctx.Target}, {(ctx.Aot ? "NativeAOT" : "single-file self-contained")}, frontend embedded in the binary"),
        };

        // Package
        if (ctx.Package)
            steps.Add(new("Package installer", PackageFormat(os)));
        else
            steps.Add(new("Package installer", PackageFormat(os), Skipped: true, SkipReason: "--no-package"));

        // Code signing (macOS + Windows have signing; Linux does not here)
        if (os is "osx" or "win")
        {
            var (signs, why) = SignState(ctx, os);
            steps.Add(signs
                ? new(SignLabel(os), "sign the packaged app so it launches without security warnings")
                : new(SignLabel(os), "sign the packaged app", Skipped: true, SkipReason: why));
        }

        // Notarization (macOS only)
        if (os == "osx")
        {
            var notarize = ctx.Package && HasMacNotarization(ctx.Config);
            steps.Add(notarize
                ? new("Notarize (Apple)", "submit the .dmg to notarytool and staple the ticket")
                : new("Notarize (Apple)", "submit to Apple notary service", Skipped: true,
                    SkipReason: !ctx.Package ? "--no-package" : "no notarization profile configured"));
        }

        // Updater artifacts
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
                ? $"one Carbon app → {PackageFormat(os)}"
                : "one Carbon app → self-contained executable (no installer)",
            Steps = steps,
        };
    }

    public async Task<int> ExecuteAsync(BundleContext ctx)
    {
        Plan(ctx).Render(dryRun: false);
        return await BuildCommand.Run(ctx.ProjectDir, ctx.Target, ctx.Aot, ctx.Package, ctx.UpdaterArtifacts);
    }

    private static string OsFamily(string target) =>
        target.StartsWith("osx") ? "osx" :
        target.StartsWith("win") ? "win" :
        target.StartsWith("linux") ? "linux" : "unknown";

    private static string PackageFormat(string os) => os switch
    {
        "osx" => ".app + .dmg",
        "win" => ".msi (+ .exe)",
        "linux" => ".AppImage",
        _ => "platform installer",
    };

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
