using System.Buffers.Binary;
using System.CommandLine;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DotCarbon.Cli.Commands;

public static class IconCommand
{
    private static readonly int[] WindowsSizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly int[] LinuxSizes = [16, 32, 48, 64, 128, 256, 512];
    private static readonly (int Size, string Type)[] MacSizes =
    [
        (16, "icp4"), (32, "icp5"), (64, "icp6"), (128, "ic07"),
        (256, "ic08"), (512, "ic09"), (1024, "ic10"),
    ];

    public static Command Build()
    {
        var command = new Command("icon", "Generate platform icons from a square PNG");
        var projectOption = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var inputOption = new Option<FileInfo?>(
            "--input", "Source PNG (default: src-carbon/icons/icon.png)");
        var outputOption = new Option<DirectoryInfo?>(
            "--output", "Output directory (default: directory containing the source PNG)");

        command.AddOption(projectOption);
        command.AddOption(inputOption);
        command.AddOption(outputOption);
        command.SetHandler(context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(projectOption)?.FullName
                ?? Directory.GetCurrentDirectory();
            var input = context.ParseResult.GetValueForOption(inputOption)?.FullName
                ?? Path.Combine(projectDir, "src-carbon", "icons", "icon.png");
            var output = context.ParseResult.GetValueForOption(outputOption)?.FullName
                ?? Path.GetDirectoryName(input)!;

            if (!Generate(input, output, out var error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Carbon] {error}");
                Console.ResetColor();
                context.ExitCode = 1;
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Carbon] Generated platform icons -> {output}");
            Console.ResetColor();
        });
        return command;
    }

    internal static bool Generate(string sourcePath, string outputDir, out string error)
    {
        error = string.Empty;
        if (!File.Exists(sourcePath))
        {
            error = $"Icon source not found: {sourcePath}";
            return false;
        }

        try
        {
            using var source = Image.Load<Rgba32>(sourcePath);
            if (source.Width != source.Height)
            {
                error = $"Icon must be square; received {source.Width}x{source.Height}.";
                return false;
            }
            if (source.Width < 512)
            {
                error = $"Icon must be at least 512x512; received {source.Width}x{source.Height}.";
                return false;
            }

            Directory.CreateDirectory(outputDir);
            var cache = new Dictionary<int, byte[]>();
            byte[] Png(int size) => cache.TryGetValue(size, out var bytes)
                ? bytes
                : cache[size] = EncodePng(source, size);

            File.WriteAllBytes(Path.Combine(outputDir, "32x32.png"), Png(32));
            File.WriteAllBytes(Path.Combine(outputDir, "128x128.png"), Png(128));
            File.WriteAllBytes(Path.Combine(outputDir, "128x128@2x.png"), Png(256));

            var linuxDir = Path.Combine(outputDir, "linux");
            Directory.CreateDirectory(linuxDir);
            foreach (var size in LinuxSizes)
                File.WriteAllBytes(Path.Combine(linuxDir, $"{size}x{size}.png"), Png(size));

            WriteIco(Path.Combine(outputDir, "icon.ico"), WindowsSizes.Select(size => (size, Png(size))).ToList());
            WriteIcns(Path.Combine(outputDir, "icon.icns"), MacSizes.Select(item => (item.Type, Png(item.Size))).ToList());
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not generate icons: {ex.Message}";
            return false;
        }
    }

    private static byte[] EncodePng(Image<Rgba32> source, int size)
    {
        using var image = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Lanczos3,
            Compand = true,
        }));
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
        return output.ToArray();
    }

    private static void WriteIco(string path, IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: false);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + images.Count * 16;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) writer.Write(png);
    }

    private static void WriteIcns(string path, IReadOnlyList<(string Type, byte[] Png)> images)
    {
        var totalLength = 8 + images.Sum(image => 8 + image.Png.Length);
        using var output = File.Create(path);
        output.Write("icns"u8);
        WriteBigEndian(output, totalLength);
        foreach (var (type, png) in images)
        {
            output.Write(Encoding.ASCII.GetBytes(type));
            WriteBigEndian(output, png.Length + 8);
            output.Write(png);
        }
    }

    private static void WriteBigEndian(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
}
