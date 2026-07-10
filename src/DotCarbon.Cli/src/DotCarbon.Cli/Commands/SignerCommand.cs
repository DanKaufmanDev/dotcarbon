using System.CommandLine;
using System.Security.Cryptography;

namespace DotCarbon.Cli.Commands;

public static class SignerCommand
{
    public static Command Build()
    {
        var command = new Command("signer", "Manage updater signing keys");
        var generate = new Command("generate", "Generate an updater signing key pair");
        var outputOption = new Option<FileInfo>(
            "--output",
            getDefaultValue: () => new FileInfo("carbon-updater.key"),
            description: "Private key output path");
        var forceOption = new Option<bool>("--force", "Overwrite an existing key pair");

        generate.AddOption(outputOption);
        generate.AddOption(forceOption);
        generate.SetHandler(context =>
        {
            context.ExitCode = Generate(
                context.ParseResult.GetValueForOption(outputOption)!,
                context.ParseResult.GetValueForOption(forceOption));
        });
        command.AddCommand(generate);
        return command;
    }

    private static int Generate(FileInfo output, bool force)
    {
        var privatePath = Path.GetFullPath(output.FullName);
        var publicPath = privatePath + ".pub";
        if (!force && (File.Exists(privatePath) || File.Exists(publicPath)))
        {
            WriteError($"Key output already exists. Use --force to replace it: {privatePath}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privatePath, key.ExportECPrivateKeyPem());
        File.WriteAllText(publicPath, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) + Environment.NewLine);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Carbon] Private key -> {privatePath}");
        Console.WriteLine($"[Carbon] Public key  -> {publicPath}");
        Console.ResetColor();
        Console.WriteLine("Keep the private key secret. Put the .pub value in bundle.updater.publicKey.");
        return 0;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
