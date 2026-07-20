using System.Security.Cryptography;
using System.Text;
using DotCarbon.Core.Host;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.7: the configured CSP is injected as a &lt;meta&gt; tag, and inline scripts/styles are hashed so
/// a strict directive (no 'unsafe-inline') still allows exactly the bundled content. A directive that
/// already permits inline content is left alone.
/// </summary>
public class CspInjectionTests
{
    private static string Sha(string content) =>
        "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content))) + "'";

    [Fact]
    public void Inline_script_is_hashed_into_a_strict_script_src()
    {
        var html = "<html><head></head><body><script>console.log(1)</script></body></html>";

        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy(html, "default-src 'self'; script-src 'self'");

        Assert.Contains($"script-src 'self' {Sha("console.log(1)")}", result);
    }

    [Fact]
    public void External_script_is_not_hashed()
    {
        var html = "<head></head><body><script src=\"/assets/app.js\"></script></body>";

        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy(html, "script-src 'self'");

        Assert.DoesNotContain("sha256", result);
    }

    [Fact]
    public void Directive_with_unsafe_inline_is_left_untouched()
    {
        var html = "<head></head><body><style>.a{color:red}</style></body>";

        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy(html, "style-src 'self' 'unsafe-inline'");

        // Adding a hash would disable 'unsafe-inline', so the author's directive is preserved verbatim.
        Assert.DoesNotContain("sha256", result);
        Assert.Contains("style-src 'self' 'unsafe-inline'", result);
    }

    [Fact]
    public void Inline_style_is_hashed_into_a_strict_style_src()
    {
        var html = "<head></head><body><style>.a{color:red}</style></body>";

        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy(html, "style-src 'self'");

        Assert.Contains($"style-src 'self' {Sha(".a{color:red}")}", result);
    }

    [Fact]
    public void An_author_provided_csp_meta_is_not_overridden()
    {
        var html = "<head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'\"></head>";

        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy(html, "script-src 'self'");

        Assert.Equal(html, result);
    }

    [Fact]
    public void Meta_is_injected_right_after_head()
    {
        var result = EmbeddedAssetStore.ApplyContentSecurityPolicy("<head></head>", "default-src 'self'");

        Assert.StartsWith("<head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'self'\">", result);
    }
}
