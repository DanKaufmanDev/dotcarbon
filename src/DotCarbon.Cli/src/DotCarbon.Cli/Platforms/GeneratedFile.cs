namespace DotCarbon.Cli.Platforms;

/// <summary>
/// A generated platform file. Carbon tracks managed files by hash; user-owned scaffolds are
/// written once and left untouched by later syncs.
/// </summary>
internal sealed record GeneratedFile(string RelativePath, string Content, bool Managed = true);
