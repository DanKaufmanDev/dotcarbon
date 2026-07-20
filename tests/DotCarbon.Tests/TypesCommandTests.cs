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

    [Fact]
    public void Generates_callable_invoke_wrappers_grouped_by_namespace()
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

                    [CarbonCommand("tick_count")]
                    public int TickCount() => 0;
                }

                public record GreetRequest(string Name);
                """);

            var result = TypesCommand.Generate(dir, syncCapabilities: false);

            Assert.NotNull(result.BindingsPath);
            var bindings = File.ReadAllText(result.BindingsPath!);

            // Wrappers reuse the declared types (DRY) and call invoke with the full command name.
            Assert.Contains("import { invoke } from '@dotcarbon/api';", bindings);
            Assert.Contains("export const app = {", bindings);
            Assert.Contains(
                "greet: (args: CarbonCommands['app:greet']['args']): Promise<CarbonCommands['app:greet']['result']> => invoke('app:greet', args),",
                bindings);
            // A no-argument command takes no parameter and passes no payload.
            Assert.Contains(
                "tick_count: (): Promise<CarbonCommands['app:tick_count']['result']> => invoke('app:tick_count'),",
                bindings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void No_bindings_option_skips_the_wrappers_file()
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
                    [CarbonCommand("ping")]
                    public string Ping() => "pong";
                }
                """);

            var result = TypesCommand.Generate(dir, syncCapabilities: false, emitBindings: false);

            Assert.Null(result.BindingsPath);
            Assert.False(File.Exists(Path.Combine(dir, "ui", "src", "carbon.gen.ts")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
