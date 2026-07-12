namespace DotCarbon.Plugins.Http;

/// <summary>Plugin config (carbon.json <c>plugins.http</c>). An empty scope allows any URL.</summary>
public record HttpOptions
{
    /// <summary>Allowed URL prefixes; a trailing <c>*</c> is a wildcard, e.g. "https://api.example.com/*".</summary>
    public string[] Scope { get; init; } = [];
}

public record FetchArgs(
    string Url,
    string? Method = null,
    Dictionary<string, string>? Headers = null,
    string? Body = null);

public record FetchResponse(
    int Status,
    string StatusText,
    Dictionary<string, string> Headers,
    string Body);
