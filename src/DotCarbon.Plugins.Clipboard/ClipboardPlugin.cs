using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Clipboard;

[CarbonPlugin("Clipboard", description: "Read, write, and clear clipboard text.")]
[CarbonPermission("clipboard:default", "Allow all clipboard commands.", Commands = new[] { "clipboard:*" })]
public partial class ClipboardPlugin : IPlugin
{
    public string Namespace => "clipboard";

    [CarbonCommand("read_text")]
    public async Task<string> ReadText()
    {
        if (OperatingSystem.IsMacOS())
            return await RunProcessOutput("pbpaste", Array.Empty<string>());

        if (OperatingSystem.IsWindows())
            return await RunProcessOutput("powershell", new[] { "-command", "Get-Clipboard" });

        return await RunProcessOutput("xclip", new[] { "-selection", "clipboard", "-o" });
    }

    [CarbonCommand("write_text")]
    public async Task WriteText(WriteTextArgs args)
    {
        if (OperatingSystem.IsMacOS())
            await RunProcessInput("pbcopy", Array.Empty<string>(), args.Text);

        else if (OperatingSystem.IsWindows())
            await RunProcessInput("clip", Array.Empty<string>(), args.Text);

        else
            await RunProcessInput("xclip", new[] { "-selection", "clipboard" }, args.Text);
    }

    [CarbonCommand("clear")]
    public async Task Clear()
    {
        await WriteText(new WriteTextArgs(string.Empty));
    }
    private static async Task<string> RunProcessOutput(string program, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.TrimEnd();
    }

    private static async Task RunProcessInput(string program, string[] args, string input)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
    }
}
