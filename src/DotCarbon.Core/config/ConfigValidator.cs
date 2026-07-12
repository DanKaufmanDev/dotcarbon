namespace DotCarbon.Core.Config;

public enum ConfigSeverity { Error, Warning }

/// <summary>A single config problem: where it is (dotted path) and what to fix.</summary>
public sealed record ConfigIssue(ConfigSeverity Severity, string Path, string Message);

/// <summary>
/// Validates a <see cref="CarbonConfig"/> for shape/consistency (not filesystem/build state) and
/// returns actionable issues. Used by <c>carbon doctor</c>; complements the build's production checks.
/// </summary>
public static class ConfigValidator
{
    private static readonly string[] KnownTargets = ["desktop", "android", "ios"];
    private static readonly string[] FileScopes = ["appData", "documents", "external"];

    public static IReadOnlyList<ConfigIssue> Validate(CarbonConfig config)
    {
        var issues = new List<ConfigIssue>();

        if (string.IsNullOrWhiteSpace(config.App.Name))
            issues.Add(new(ConfigSeverity.Error, "app.name", "is required."));

        if (!IsReverseDns(config.App.Identifier))
            issues.Add(new(ConfigSeverity.Error, "app.identifier",
                $"must be a reverse-DNS identifier like com.example.app (got '{config.App.Identifier}')."));

        if (string.IsNullOrWhiteSpace(config.App.Version))
            issues.Add(new(ConfigSeverity.Error, "app.version", "is required."));
        else if (!IsSemver(config.App.Version))
            issues.Add(new(ConfigSeverity.Warning, "app.version",
                $"should be a semantic version like 1.2.3 (got '{config.App.Version}')."));

        if (config.Bundle.Targets.Count == 0)
            issues.Add(new(ConfigSeverity.Warning, "bundle.targets", "is empty; defaulting to [\"desktop\"]."));
        foreach (var target in config.Bundle.Targets.Where(t => !KnownTargets.Contains(t, StringComparer.OrdinalIgnoreCase)))
            issues.Add(new(ConfigSeverity.Error, "bundle.targets",
                $"unknown target '{target}' (expected desktop, android, or ios)."));

        if (config.Window.Width <= 0 || config.Window.Height <= 0)
            issues.Add(new(ConfigSeverity.Error, "window", "width and height must be positive."));
        if (config.Window.MinWidth is int minW && config.Window.MaxWidth is int maxW && minW > maxW)
            issues.Add(new(ConfigSeverity.Warning, "window", "minWidth is greater than maxWidth."));
        if (config.Window.MinHeight is int minH && config.Window.MaxHeight is int maxH && minH > maxH)
            issues.Add(new(ConfigSeverity.Warning, "window", "minHeight is greater than maxHeight."));

        var android = config.Bundle.Android;
        if (android.MinSdk > android.TargetSdk)
            issues.Add(new(ConfigSeverity.Warning, "bundle.android",
                $"minSdk ({android.MinSdk}) is greater than targetSdk ({android.TargetSdk})."));

        if (config.Permissions.Files is { } files &&
            !FileScopes.Contains(files, StringComparer.OrdinalIgnoreCase))
            issues.Add(new(ConfigSeverity.Error, "permissions.files",
                $"must be appData, documents, or external (got '{files}')."));

        AddDuplicate(issues, "bundle.fileAssociations",
            config.Bundle.FileAssociations.SelectMany(a => a.Extensions).Select(e => e.TrimStart('.').ToLowerInvariant()),
            "extension");
        AddDuplicate(issues, "bundle.protocols",
            config.Bundle.Protocols.SelectMany(p => p.Schemes).Select(s => s.ToLowerInvariant()),
            "scheme");

        if (config.Bundle.Updater.Active)
        {
            if (config.Bundle.Updater.Endpoints.Count == 0)
                issues.Add(new(ConfigSeverity.Error, "bundle.updater", "active requires at least one endpoint."));
            if (string.IsNullOrWhiteSpace(config.Bundle.Updater.PublicKey))
                issues.Add(new(ConfigSeverity.Error, "bundle.updater", "active requires publicKey."));
        }

        return issues;
    }

    private static void AddDuplicate(List<ConfigIssue> issues, string path, IEnumerable<string> values, string kind)
    {
        var duplicate = values.Where(v => v.Length > 0).GroupBy(v => v).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            issues.Add(new(ConfigSeverity.Error, path, $"duplicate {kind}: {duplicate.Key}."));
    }

    private static bool IsReverseDns(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Contains('.') &&
        !id.StartsWith('.') && !id.EndsWith('.') &&
        id.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_');

    private static bool IsSemver(string version)
    {
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length is >= 2 and <= 3 && parts.All(part => part.Length > 0 && part.All(char.IsDigit));
    }
}
