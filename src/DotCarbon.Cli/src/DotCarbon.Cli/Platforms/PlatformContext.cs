using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Platforms;

/// <summary>Inputs a platform generator needs: the app config and where the shell lives.</summary>
internal sealed record PlatformContext(CarbonConfig Config, string WorkingDir, string PlatformDir);
