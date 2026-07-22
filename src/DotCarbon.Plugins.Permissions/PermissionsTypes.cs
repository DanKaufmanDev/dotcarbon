namespace DotCarbon.Plugins.Permissions;

/// <summary>
/// The capability to check or prompt for: "camera", "microphone", "location", "notifications",
/// "contacts" or "photoLibrary" — the same ids used in carbon.json's <c>permissions</c> block.
/// </summary>
public record PermissionArgs(string Permission);
