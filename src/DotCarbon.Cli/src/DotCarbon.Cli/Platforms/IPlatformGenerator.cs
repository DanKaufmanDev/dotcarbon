using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Platforms;

/// <summary>
/// Generates a native platform shell (Android/iOS/desktop) from the Carbon app model.
/// Generators are pure: given a config, they return the same files — that reproducibility
/// is what lets sync detect drift and preserve user edits.
/// </summary>
internal interface IPlatformGenerator
{
    /// <summary>CLI id: <c>android</c>, <c>ios</c>, <c>desktop</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>A short, stable signature of the config inputs this generator depends on.</summary>
    string ConfigSignature(CarbonConfig config);

    /// <summary>The full set of files for the shell.</summary>
    IReadOnlyList<GeneratedFile> Generate(PlatformContext context);
}
