using System.CommandLine;
using System.Reflection;
using System.Text;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon plugin new</c> — scaffold a third-party plugin: the C# project, a JSON serializer
/// context, a permission manifest, a test project and the JS package. Everything the first-party
/// plugins do by hand, so a plugin author does not have to reverse-engineer the pattern.
/// </summary>
public static class PluginCommand
{
    public static Command Build()
    {
        var command = new Command("plugin", "Author DotCarbon plugins");
        command.AddCommand(NewSubcommand());
        return command;
    }

    private static Command NewSubcommand()
    {
        var command = new Command("new", "Scaffold a new plugin project");

        var nameArgument = new Argument<string>("name", "Plugin name, e.g. confetti or acme-confetti");
        var outputOption = new Option<DirectoryInfo?>(
            "--output", "Directory to create the plugin in (default: ./carbon-plugin-<name>)");
        var namespaceOption = new Option<string?>(
            "--namespace", "Command namespace (default: the plugin name)");
        var idOption = new Option<string?>(
            "--id", "C# project, assembly and namespace name (default: CarbonPlugin.<Name>)");
        var npmOption = new Option<string?>(
            "--npm", "npm package name (default: carbon-plugin-<name>)");
        var commandsOption = new Option<string?>(
            "--commands", "Comma-separated commands to scaffold (default: ping,echo)");
        var versionOption = new Option<string?>(
            "--carbon-version", "DotCarbon package version to reference (default: this CLI's version)");
        var noJsOption = new Option<bool>("--no-js", "Skip the JS package");
        var noTestsOption = new Option<bool>("--no-tests", "Skip the test project");
        var forceOption = new Option<bool>("--force", "Overwrite files that already exist");
        var dryRunOption = new Option<bool>("--dry-run", "Print what would be written without writing it");

        command.AddArgument(nameArgument);
        foreach (var option in new Option[]
        {
            outputOption, namespaceOption, idOption, npmOption, commandsOption,
            versionOption, noJsOption, noTestsOption, forceOption, dryRunOption,
        })
        {
            command.AddOption(option);
        }

        command.SetHandler(context =>
        {
            var parsed = context.ParseResult;
            var name = parsed.GetValueForArgument(nameArgument);
            var commands = parsed.GetValueForOption(commandsOption)
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var request = new PluginRequest(
                name,
                parsed.GetValueForOption(outputOption)?.FullName,
                parsed.GetValueForOption(namespaceOption),
                parsed.GetValueForOption(idOption),
                parsed.GetValueForOption(npmOption),
                commands is { Length: > 0 } ? commands : ["ping", "echo"],
                parsed.GetValueForOption(versionOption) ?? CarbonVersion(),
                !parsed.GetValueForOption(noJsOption),
                !parsed.GetValueForOption(noTestsOption),
                parsed.GetValueForOption(forceOption),
                parsed.GetValueForOption(dryRunOption));

            context.ExitCode = Run(request) ? 0 : 1;
        });

        return command;
    }

    internal sealed record PluginRequest(
        string Name,
        string? Output,
        string? Namespace,
        string? Id,
        string? NpmPackage,
        IReadOnlyList<string> Commands,
        string CarbonVersion,
        bool IncludeJs,
        bool IncludeTests,
        bool Force,
        bool DryRun);

    /// <summary>The naming a plugin needs to be consistent across C#, npm and the command bridge.</summary>
    internal sealed record PluginNames(
        string Kebab, string Pascal, string Namespace, string ProjectId, string NpmPackage, string ClassName);

    internal static PluginNames Resolve(PluginRequest request)
    {
        var kebab = Kebab(request.Name);
        var pascal = Pascal(kebab);
        return new PluginNames(
            kebab,
            pascal,
            request.Namespace ?? kebab,
            // Deliberately not DotCarbon.* — that prefix is the first-party packages, and a
            // third-party plugin squatting it makes support questions ambiguous.
            request.Id ?? $"CarbonPlugin.{pascal}",
            request.NpmPackage ?? $"carbon-plugin-{kebab}",
            $"{pascal}Plugin");
    }

    internal static bool Run(PluginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || Kebab(request.Name).Length == 0)
        {
            Write("Plugin name must contain at least one letter or digit.", ConsoleColor.Red);
            return false;
        }

        var names = Resolve(request);
        var root = request.Output ?? Path.Combine(Directory.GetCurrentDirectory(), $"carbon-plugin-{names.Kebab}");
        var commands = request.Commands.Select(Kebab).Where(name => name.Length > 0).Distinct().ToList();
        if (commands.Count == 0)
        {
            Write("No valid command names to scaffold.", ConsoleColor.Red);
            return false;
        }

        var testProject = $"{names.ProjectId}.Tests";
        var files = new List<(string Path, string Content)>
        {
            (Path.Combine(root, names.ProjectId, $"{names.ProjectId}.csproj"),
                Csproj(names, request.CarbonVersion, request.IncludeTests ? testProject : null)),
            (Path.Combine(root, names.ProjectId, $"{names.ClassName}.cs"), PluginClass(names, commands)),
            (Path.Combine(root, names.ProjectId, $"{names.Pascal}Types.cs"), Types(names)),
            (Path.Combine(root, names.ProjectId, $"{names.Pascal}JsonContext.cs"), JsonContext(names)),
            (Path.Combine(root, "permissions", $"{names.Namespace}.json"), PermissionManifest(names, commands)),
            (Path.Combine(root, "README.md"), Readme(names, commands, request.CarbonVersion)),
            (Path.Combine(root, ".gitignore"), Gitignore()),
        };

        if (request.IncludeTests)
        {
            files.Add((Path.Combine(root, "tests", testProject, $"{testProject}.csproj"),
                TestCsproj(names, testProject)));
            files.Add((Path.Combine(root, "tests", testProject, $"{names.ClassName}Tests.cs"),
                Tests(names, commands)));
        }

        if (request.IncludeJs)
        {
            files.Add((Path.Combine(root, "js", "package.json"), PackageJson(names)));
            files.Add((Path.Combine(root, "js", "tsconfig.json"), TsConfig()));
            files.Add((Path.Combine(root, "js", "src", "index.ts"), IndexTs(names, commands)));
        }

        Info($"Plugin:    {names.Pascal} ({names.ProjectId})");
        Info($"Namespace: {names.Namespace}  ->  {string.Join(", ", commands.Select(c => $"{names.Namespace}:{c}"))}");
        Info($"npm:       {names.NpmPackage}");

        foreach (var (path, content) in files)
        {
            var relative = Path.GetRelativePath(root, path);
            if (File.Exists(path) && !request.Force)
            {
                Write($"skipped {relative} (already exists — pass --force to overwrite)", ConsoleColor.Yellow);
                continue;
            }

            if (request.DryRun)
            {
                Info($"would write {relative}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            Write($"wrote {relative}", ConsoleColor.Green);
        }

        Console.WriteLine();
        Console.WriteLine("[Carbon] Next steps:");
        Console.WriteLine($"  1. Build:  dotnet build {Path.GetFileName(root)}/{names.ProjectId}");
        if (request.IncludeTests)
            Console.WriteLine($"  2. Test:   dotnet test {Path.GetFileName(root)}/tests/{testProject}");
        Console.WriteLine("  3. Use it: see README.md — reference the project, call " +
                          $"app.UsePlugin<{names.ClassName}>(), and copy permissions/" +
                          $"{names.Namespace}.json into the app's src-carbon/permissions/.");
        return true;
    }

    private static string Csproj(PluginNames names, string version, string? testProject) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        // Carbon apps can publish NativeAOT, so a plugin that is not AOT-clean breaks its consumers.
        "    <IsAotCompatible>true</IsAotCompatible>\n" +
        $"    <PackageId>{names.ProjectId}</PackageId>\n" +
        "    <Version>0.1.0</Version>\n" +
        $"    <Description>{names.Pascal} plugin for DotCarbon.</Description>\n" +
        "  </PropertyGroup>\n\n" +
        "  <ItemGroup>\n" +
        $"    <PackageReference Include=\"DotCarbon.Core\" Version=\"{version}\" />\n" +
        "  </ItemGroup>\n\n" +
        (testProject is null
            ? string.Empty
            : "  <ItemGroup>\n" +
              $"    <InternalsVisibleTo Include=\"{testProject}\" />\n" +
              "  </ItemGroup>\n\n") +
        "  <!-- The permission manifest consumers copy into their app; also shipped in the package. -->\n" +
        "  <ItemGroup>\n" +
        "    <None Include=\"..\\permissions\\*.json\" Pack=\"true\" PackagePath=\"permissions\" Visible=\"false\" />\n" +
        "  </ItemGroup>\n\n" +
        "</Project>\n";

    private static string PluginClass(PluginNames names, IReadOnlyList<string> commands)
    {
        var body = new StringBuilder();
        foreach (var command in commands)
        {
            var method = Pascal(command);
            body.Append($"\n    [CarbonCommand(\"{command}\")]\n");
            body.Append(command == "echo"
                ? $"    public {names.Pascal}Result Echo({names.Pascal}Args args) => new(args.Message, args.Message.Length);\n"
                : $"    public string {method}() => \"{names.Namespace}:{command} ok\";\n");
        }

        return "using DotCarbon.Core.Bridge;\n" +
               "using DotCarbon.Core.Plugins;\n\n" +
               $"namespace {names.ProjectId};\n\n" +
               "/// <summary>\n" +
               $"/// The {names.Pascal} plugin's command surface. Every [CarbonCommand] method becomes\n" +
               $"/// <c>{names.Namespace}:&lt;name&gt;</c> on the JS bridge.\n" +
               "/// </summary>\n" +
               $"[CarbonPlugin(\"{names.Pascal}\", description: \"{names.Pascal} plugin for DotCarbon.\")]\n" +
               "[CarbonPluginPlatform(\"desktop\", \"android\", \"ios\")]\n" +
               $"[CarbonPermission(\"{names.Namespace}:default\", \"Allow {names.Namespace} commands.\", " +
               $"Commands = new[] {{ \"{names.Namespace}:*\" }})]\n" +
               "// `partial` is required: the Carbon source generator emits the command registration into\n" +
               "// this class, which is what keeps the bridge reflection-free (and NativeAOT-safe).\n" +
               $"public partial class {names.ClassName} : IPlugin\n" +
               "{\n" +
               $"    public string Namespace => \"{names.Namespace}\";\n" +
               body +
               "}\n";
    }

    private static string Types(PluginNames names) =>
        $"namespace {names.ProjectId};\n\n" +
        "/// <summary>Arguments for a command. Records keep the JS payload shape obvious.</summary>\n" +
        $"public record {names.Pascal}Args(string Message);\n\n" +
        "/// <summary>What the command returns. Serialized to JSON with camelCase property names.</summary>\n" +
        $"public record {names.Pascal}Result(string Message, int Length);\n";

    private static string JsonContext(PluginNames names) =>
        "using System.Text.Json.Serialization;\n\n" +
        $"namespace {names.ProjectId};\n\n" +
        "// Source-generated serialization for this plugin's types: the bridge stays reflection-free, so\n" +
        "// commands keep working in a NativeAOT-published app. Add every command argument and result type.\n" +
        "[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, " +
        "PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]\n" +
        $"[JsonSerializable(typeof({names.Pascal}Args))]\n" +
        $"[JsonSerializable(typeof({names.Pascal}Result))]\n" +
        $"internal partial class {names.Pascal}JsonContext : JsonSerializerContext;\n";

    /// <summary>
    /// The manifest `carbon capabilities` discovers, so this plugin's permissions show up without a
    /// hardcoded entry in the CLI's first-party catalog.
    /// </summary>
    private static string PermissionManifest(PluginNames names, IReadOnlyList<string> commands)
    {
        var granted = string.Join(", ", commands.Select(command => $"\"{names.Namespace}:{command}\""));
        return "{\n" +
               $"    \"namespace\": \"{names.Namespace}\",\n" +
               $"    \"package\": \"{names.ProjectId}\",\n" +
               "    \"platforms\": [\"desktop\", \"android\", \"ios\"],\n" +
               "    \"permissions\": [\n" +
               "        {\n" +
               $"            \"identifier\": \"{names.Namespace}:default\",\n" +
               $"            \"description\": \"Allow {names.Namespace} commands.\",\n" +
               $"            \"commands\": [{granted}]\n" +
               "        }\n" +
               "    ]\n" +
               "}\n";
    }

    private static string TestCsproj(PluginNames names, string testProject) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "    <IsPackable>false</IsPackable>\n" +
        "    <IsTestProject>true</IsTestProject>\n" +
        $"    <AssemblyName>{testProject}</AssemblyName>\n" +
        "  </PropertyGroup>\n\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.11.1\" />\n" +
        "    <PackageReference Include=\"xunit\" Version=\"2.9.2\" />\n" +
        "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"2.8.2\" />\n" +
        "  </ItemGroup>\n\n" +
        "  <ItemGroup>\n" +
        $"    <ProjectReference Include=\"..\\..\\{names.ProjectId}\\{names.ProjectId}.csproj\" />\n" +
        "  </ItemGroup>\n\n" +
        "</Project>\n";

    private static string Tests(PluginNames names, IReadOnlyList<string> commands)
    {
        var assertions = new StringBuilder();
        foreach (var command in commands)
            assertions.Append($"        Assert.Contains(\"{names.Namespace}:{command}\", registry.Handlers.Keys);\n");

        var echoTest = commands.Contains("echo")
            ? "\n    [Fact]\n" +
              "    public void Echo_returns_the_message_and_its_length()\n" +
              "    {\n" +
              $"        var result = new {names.ClassName}().Echo(new {names.Pascal}Args(\"hello\"));\n\n" +
              "        Assert.Equal(\"hello\", result.Message);\n" +
              "        Assert.Equal(5, result.Length);\n" +
              "    }\n"
            : string.Empty;

        return "using System.Text.Json;\n" +
               "using System.Text.Json.Nodes;\n" +
               "using DotCarbon.Core.Bridge;\n" +
               $"using {names.ProjectId};\n" +
               "using Xunit;\n\n" +
               $"namespace {names.ProjectId}.Tests;\n\n" +
               $"public class {names.ClassName}Tests\n" +
               "{\n" +
               "    [Fact]\n" +
               "    public void Registers_its_commands()\n" +
               "    {\n" +
               "        var registry = new FakeRegistry();\n\n" +
               $"        new {names.ClassName}().Register(registry);\n\n" +
               assertions +
               "    }\n" +
               echoTest +
               "\n    /// <summary>Captures what the plugin registers, without booting an app.</summary>\n" +
               "    private sealed class FakeRegistry : ICommandRegistry\n" +
               "    {\n" +
               "        public Dictionary<string, Func<JsonElement, Task<JsonNode?>>> Handlers { get; } =\n" +
               "            new(StringComparer.Ordinal);\n\n" +
               "        public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler) =>\n" +
               "            Handlers[name] = handler;\n" +
               "    }\n" +
               "}\n";
    }

    private static string PackageJson(PluginNames names) =>
        "{\n" +
        $"    \"name\": \"{names.NpmPackage}\",\n" +
        "    \"version\": \"0.1.0\",\n" +
        $"    \"description\": \"JS bindings for the {names.Pascal} DotCarbon plugin\",\n" +
        "    \"type\": \"module\",\n" +
        "    \"main\": \"./dist/index.js\",\n" +
        "    \"types\": \"./dist/index.d.ts\",\n" +
        "    \"exports\": {\n" +
        "        \".\": {\n" +
        "            \"import\": \"./dist/index.js\",\n" +
        "            \"types\": \"./dist/index.d.ts\"\n" +
        "        }\n" +
        "    },\n" +
        "    \"scripts\": {\n" +
        "        \"build\": \"tsc\",\n" +
        "        \"prepublishOnly\": \"npm run build\"\n" +
        "    },\n" +
        "    \"dependencies\": {\n" +
        "        \"@dotcarbon/api\": \"latest\"\n" +
        "    },\n" +
        "    \"devDependencies\": {\n" +
        "        \"typescript\": \"^5.0.0\"\n" +
        "    },\n" +
        "    \"files\": [\"dist\"],\n" +
        "    \"license\": \"MIT\"\n" +
        "}\n";

    private static string TsConfig() =>
        "{\n" +
        "    \"compilerOptions\": {\n" +
        "        \"target\": \"ES2020\",\n" +
        "        \"module\": \"ESNext\",\n" +
        "        \"moduleResolution\": \"bundler\",\n" +
        "        \"declaration\": true,\n" +
        "        \"outDir\": \"./dist\",\n" +
        "        \"strict\": true,\n" +
        "        \"lib\": [\"ES2020\", \"DOM\"]\n" +
        "    },\n" +
        "    \"include\": [\"src\"]\n" +
        "}\n";

    private static string IndexTs(PluginNames names, IReadOnlyList<string> commands)
    {
        var exports = new StringBuilder();
        var declarations = new StringBuilder();

        foreach (var command in commands)
        {
            var fn = Camel(command);
            if (command == "echo")
            {
                exports.Append($"export const {fn} = (message: string): Promise<{names.Pascal}Result> =>\n" +
                               $"    invoke('{names.Namespace}:{command}', {{ message }})\n\n");
                declarations.Append($"        '{names.Namespace}:{command}': " +
                                    $"{{ args: {{ message: string }}; result: {names.Pascal}Result }}\n");
            }
            else
            {
                exports.Append($"export const {fn} = (): Promise<string> => invoke('{names.Namespace}:{command}')\n\n");
                declarations.Append($"        '{names.Namespace}:{command}': {{ args: void; result: string }}\n");
            }
        }

        return "import { invoke } from '@dotcarbon/api'\n\n" +
               $"export interface {names.Pascal}Result {{\n" +
               "    message: string\n" +
               "    length: number\n" +
               "}\n\n" +
               exports +
               "// Augments the bridge's command map, so `invoke` is typed for consumers of this package.\n" +
               "declare module '@dotcarbon/api' {\n" +
               "    interface CarbonCommands {\n" +
               declarations +
               "    }\n" +
               "}\n";
    }

    private static string Readme(PluginNames names, IReadOnlyList<string> commands, string version)
    {
        var list = string.Join("\n", commands.Select(command => $"- `{names.Namespace}:{command}`"));
        return $"# {names.NpmPackage}\n\n" +
               $"A DotCarbon plugin. Commands:\n\n{list}\n\n" +
               "## Install (in a Carbon app)\n\n" +
               "```bash\n" +
               $"dotnet add src-carbon package {names.ProjectId}\n" +
               $"npm add {names.NpmPackage}\n" +
               "```\n\n" +
               "Register it in `src-carbon/Program.cs`:\n\n" +
               "```csharp\n" +
               $"using {names.ProjectId};\n\n" +
               "CarbonApp.Create(config)\n" +
               "    .UseDesktop()\n" +
               $"    .UsePlugin<{names.ClassName}>()\n" +
               "    .Run();\n" +
               "```\n\n" +
               $"Copy `permissions/{names.Namespace}.json` into the app's `src-carbon/permissions/` so " +
               "`carbon capabilities` can discover this plugin's permissions, then grant it:\n\n" +
               "```bash\n" +
               $"carbon capabilities add {names.Namespace}:default\n" +
               "```\n\n" +
               "Call it from the frontend:\n\n" +
               "```ts\n" +
               $"import {{ {Camel(commands[0])} }} from '{names.NpmPackage}'\n\n" +
               $"await {Camel(commands[0])}()\n" +
               "```\n\n" +
               "## Develop\n\n" +
               "```bash\n" +
               $"dotnet build {names.ProjectId}\n" +
               "dotnet test tests\n" +
               "npm --prefix js install && npm --prefix js run build\n" +
               "```\n\n" +
               $"Built against DotCarbon.Core {version}.\n";
    }

    private static string Gitignore() =>
        "bin/\nobj/\ndist/\nnode_modules/\n*.user\n";

    private static string CarbonVersion()
    {
        var informational = typeof(PluginCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "*" : version;
    }

    /// <summary>"AcmeConfetti", "acme_confetti" and "Acme Confetti" all become "acme-confetti".</summary>
    internal static string Kebab(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsLetterOrDigit(character))
            {
                var previous = index > 0 ? value[index - 1] : '\0';
                if (char.IsUpper(character) && builder.Length > 0 && (char.IsLower(previous) || char.IsDigit(previous)))
                    builder.Append('-');
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    internal static string Pascal(string kebab)
    {
        var parts = kebab.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string Camel(string kebab)
    {
        var pascal = Pascal(kebab);
        return pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static void Info(string message) => Console.WriteLine($"[Carbon] {message}");

    private static void Write(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
