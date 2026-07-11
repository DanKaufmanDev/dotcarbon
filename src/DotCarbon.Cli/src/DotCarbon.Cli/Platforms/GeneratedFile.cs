namespace DotCarbon.Cli.Platforms;

/// <summary>
/// One file a platform generator emits. <see cref="Managed"/> files are owned by Carbon:
/// tracked by hash and regenerated on <c>carbon platform sync</c> (unless the user edited
/// them). Non-managed files are user-editable scaffolds — written once, never overwritten.
/// </summary>
internal sealed record GeneratedFile(string RelativePath, string Content, bool Managed = true);
