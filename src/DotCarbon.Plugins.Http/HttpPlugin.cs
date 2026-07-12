using System.Text;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Http;

[CarbonPlugin("Http", description: "Make scoped HTTP requests from the backend.")]
[CarbonPermission("http:default", "Allow all HTTP commands.", Commands = new[] { "http:*" })]
public partial class HttpPlugin : IPlugin
{
    private static readonly HttpClient Client = new();
    private HttpOptions _options = new();

    public string Namespace => "http";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(HttpJsonContext.Default.HttpOptions) ?? new();
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("fetch")]
    public async Task<FetchResponse> Fetch(FetchArgs args)
    {
        if (!HttpScope.IsAllowed(args.Url, _options.Scope))
            throw new InvalidOperationException(
                $"URL '{args.Url}' is blocked by the http scope. Add a matching entry to plugins.http.scope.");

        var method = string.IsNullOrWhiteSpace(args.Method) ? "GET" : args.Method;
        using var request = new HttpRequestMessage(new HttpMethod(method), args.Url);

        if (args.Body is not null)
            request.Content = new StringContent(args.Body, Encoding.UTF8);
        if (args.Headers is not null)
            foreach (var (key, value) in args.Headers)
                if (!request.Headers.TryAddWithoutValidation(key, value))
                    request.Content?.Headers.TryAddWithoutValidation(key, value);

        using var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            headers[header.Key] = string.Join(", ", header.Value);

        return new FetchResponse((int)response.StatusCode, response.ReasonPhrase ?? string.Empty, headers, body);
    }
}
