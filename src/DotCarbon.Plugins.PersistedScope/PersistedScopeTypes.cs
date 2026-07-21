namespace DotCarbon.Plugins.PersistedScope;

/// <summary>Grant a path to a scope ("fs" or "asset") and persist it across restarts.</summary>
public record ScopeGrant(string Scope, string Path);

/// <summary>Plugin configuration (<c>plugins.persisted-scope</c>). <c>File</c> overrides the store path.</summary>
public record PersistedScopeOptions(string? File = null);
