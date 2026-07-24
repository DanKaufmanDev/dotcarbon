using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Everything a bundler target needs to plan and execute a bundle. One app shape
/// (carbon.json + src-carbon + ui) drives every platform target.
/// </summary>
internal sealed record BundleContext(
    CarbonConfig Config,
    string WorkingDir,
    DirectoryInfo? ProjectDir,
    string Target,
    bool Aot,
    bool Package,
    bool UpdaterArtifacts,
    bool DryRun,
    /// <summary>Publish the Debug configuration instead of Release (faster, unoptimized).</summary>
    bool Debug = false,
    /// <summary>CLI override for the per-OS bundle formats; null means use carbon.json.</summary>
    IReadOnlyList<string>? Formats = null);
