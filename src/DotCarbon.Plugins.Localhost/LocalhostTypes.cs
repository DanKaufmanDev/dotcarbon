namespace DotCarbon.Plugins.Localhost;

/// <summary>Plugin configuration (<c>plugins.localhost</c>). <c>Port</c> 0 picks a free port.</summary>
public record LocalhostOptions(int Port = 0);

/// <summary>Start (or restart) the server on a port; 0 picks a free one.</summary>
public record LocalhostStartArgs(int Port = 0);
