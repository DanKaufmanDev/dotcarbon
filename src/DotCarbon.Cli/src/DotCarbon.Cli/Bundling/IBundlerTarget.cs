namespace DotCarbon.Cli.Bundling;

/// <summary>
/// One platform bundle target (desktop, android, ios). Every target shares the same
/// Carbon app model and produces its own platform-specific output. Separating frontend
/// build, .NET publish, platform packaging, signing, notarization and artifacts into a
/// described plan is the whole point of the abstraction: <see cref="Plan"/> answers
/// "what will happen", <see cref="ExecuteAsync"/> does it.
/// </summary>
internal interface IBundlerTarget
{
    /// <summary>Stable id used on the CLI: <c>desktop</c>, <c>android</c>, <c>ios</c>.</summary>
    string Id { get; }

    /// <summary>Human-friendly name for logs and the plan header.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether this target can run on the current machine/config. When false, <paramref name="reason"/>
    /// is an actionable message (e.g. "not implemented yet — roadmap Phase 5").
    /// </summary>
    bool IsSupported(BundleContext context, out string? reason);

    /// <summary>Describe every step, in order, before anything runs.</summary>
    BundlePlan Plan(BundleContext context);

    /// <summary>Run the plan. Returns a process exit code (0 = success).</summary>
    Task<int> ExecuteAsync(BundleContext context);
}
