using System.Text.Json.Nodes;
using Xunit;

namespace DotCarbon.Tests;

public class CreateAppTemplateTests
{
    [Fact]
    public void Frontend_templates_install_the_latest_dotcarbon_api()
    {
        var templates = Path.Combine(FindRepoRoot(), "dotcarbon-js", "packages", "create-app", "templates");
        var packageFiles = Directory.GetFiles(templates, "package.json", SearchOption.AllDirectories);

        Assert.NotEmpty(packageFiles);
        foreach (var packageFile in packageFiles)
        {
            var package = JsonNode.Parse(File.ReadAllText(packageFile))!.AsObject();
            Assert.Equal(
                "latest",
                package["dependencies"]?["@dotcarbon/api"]?.GetValue<string>());
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotCarbon.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find DotCarbon.slnx.");
    }
}
