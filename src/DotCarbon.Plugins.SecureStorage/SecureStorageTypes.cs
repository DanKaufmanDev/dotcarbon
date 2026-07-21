namespace DotCarbon.Plugins.SecureStorage;

/// <summary>Store a secret value under a key.</summary>
public record SecretArgs(string Key, string Value);

/// <summary>Address a stored secret by key.</summary>
public record KeyArgs(string Key);
