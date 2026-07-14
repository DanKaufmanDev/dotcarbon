using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

public class TypesCommandTests
{
    [Fact]
    public void Generated_metadata_uses_its_structural_type_without_an_api_metadata_import()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-types-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(dir, "src-carbon");
        Directory.CreateDirectory(sourceDir);
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Commands.cs"),
                """
                public sealed class AppCommands : IPlugin
                {
                    public string Namespace => "app";

                    [CarbonCommand("greet")]
                    public string Greet(GreetRequest request) => request.Name;
                }

                public record GreetRequest(string Name);
                """);

            var result = TypesCommand.Generate(dir, syncCapabilities: false);
            var declarations = File.ReadAllText(result.TargetPath);

            Assert.DoesNotContain("import type { CarbonPluginMetadata }", declarations);
            Assert.Contains(
                "export declare const carbonGeneratedPluginMetadata: CarbonGeneratedPluginMetadata;",
                declarations);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
