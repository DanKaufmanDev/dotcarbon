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
    bool DryRun);
