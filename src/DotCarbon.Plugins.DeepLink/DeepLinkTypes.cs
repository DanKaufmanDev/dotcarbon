namespace DotCarbon.Plugins.DeepLink;

public record DeepLinkConfig(string[] Schemes);

public record DeepLinkInfo(string[] Schemes, string[] PendingUrls);
