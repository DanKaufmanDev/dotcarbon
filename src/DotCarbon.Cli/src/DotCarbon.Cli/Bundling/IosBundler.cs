namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Reserved iOS target. Slots into the same bundler abstraction as desktop/android;
/// the WKWebView host + packaging land in roadmap Phase 6.
/// </summary>
internal sealed class IosBundler : IBundlerTarget
{
    public string Id => "ios";
    public string DisplayName => "iOS";

    public bool IsSupported(BundleContext context, out string? reason)
    {
        reason = "iOS bundling is not implemented yet (roadmap Phase 6: iOS host — simulator/device/archive).";
        return false;
    }

    public BundlePlan Plan(BundleContext ctx) => new()
    {
        TargetId = Id,
        TargetName = DisplayName,
        Summary = "reserved — iOS host lands in roadmap Phase 6",
        Steps = new[]
        {
            new BundleStep("iOS bundle", "simulator/device/archive via .NET iOS + WKWebView",
                Skipped: true, SkipReason: "not implemented yet"),
        },
    };

    public Task<int> ExecuteAsync(BundleContext ctx)
    {
        IsSupported(ctx, out var reason);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[Carbon] {reason}");
        Console.ResetColor();
        return Task.FromResult(1);
    }
}
