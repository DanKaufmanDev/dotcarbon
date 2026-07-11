namespace DotCarbon.Cli.Bundling;

/// <summary>A single, ordered step in a bundle. Steps are described before anything runs.</summary>
internal sealed record BundleStep(string Title, string Detail, bool Skipped = false, string? SkipReason = null);

/// <summary>
/// The full, inspectable plan for one bundle target: every step, in order, with the
/// reason any step is skipped. Rendered by <c>--dry-run</c> and printed before a real run.
/// </summary>
internal sealed class BundlePlan
{
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<BundleStep> Steps { get; init; }

    public void Render(bool dryRun)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n⚡ Carbon bundle plan — {TargetName}");
        Console.ResetColor();
        Console.WriteLine($"   {Summary}");
        Console.WriteLine();

        var active = Steps.Where(s => !s.Skipped).ToList();
        var n = 0;
        foreach (var step in Steps)
        {
            if (step.Skipped)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   -   {step.Title} — skipped ({step.SkipReason})");
                Console.ResetColor();
            }
            else
            {
                n++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"   {n}/{active.Count} ");
                Console.ResetColor();
                Console.WriteLine($"{step.Title}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"         {step.Detail}");
                Console.ResetColor();
            }
        }

        if (dryRun)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n   (dry run — nothing was executed)");
            Console.ResetColor();
        }
        Console.WriteLine();
    }
}
