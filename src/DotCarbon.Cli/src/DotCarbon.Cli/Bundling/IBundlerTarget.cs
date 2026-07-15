namespace DotCarbon.Cli.Bundling;

/// <summary>
/// A platform bundler that can describe its work before producing an artifact.
/// </summary>
internal interface IBundlerTarget
{
    /// <summary>Stable id used on the CLI: <c>desktop</c>, <c>android</c>, <c>ios</c>.</summary>
    string Id { get; }

    /// <summary>Human-friendly name for logs and the plan header.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this target can run on the current machine/config. When false, <paramref name="reason"/>
    /// explains what the user needs to change.
    /// </summary>
    bool IsSupported(BundleContext context, out string? reason);

    /// <summary>Describe every step, in order, before anything runs.</summary>
    BundlePlan Plan(BundleContext context);

    /// <summary>Run the plan. Returns a process exit code (0 = success).</summary>
    Task<int> ExecuteAsync(BundleContext context);
}
