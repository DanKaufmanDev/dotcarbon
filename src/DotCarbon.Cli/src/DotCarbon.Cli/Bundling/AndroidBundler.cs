namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Reserved Android target. The bundler architecture treats Android as just another
/// target so it slots in without a rewrite; the host + packaging land in roadmap Phase 5.
/// </summary>
internal sealed class AndroidBundler : IBundlerTarget
{
    public string Id => "android";
    public string DisplayName => "Android";

    public bool IsSupported(BundleContext context, out string? reason)
    {
        reason = "Android bundling is not implemented yet (roadmap Phase 5: Android host — APK/AAB).";
        return false;
    }

    public BundlePlan Plan(BundleContext ctx) => new()
    {
        TargetId = Id,
        TargetName = DisplayName,
        Summary = "reserved — Android host lands in roadmap Phase 5",
        Steps = new[]
        {
            new BundleStep("Android bundle", "APK/AAB via .NET Android + Android WebView",
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
