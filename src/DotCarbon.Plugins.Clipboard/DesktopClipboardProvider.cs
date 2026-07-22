using System.Diagnostics;

namespace DotCarbon.Plugins.Clipboard;

/// <summary>Desktop clipboard access via the platform CLI tools (pbcopy/pbpaste, clip/Get-Clipboard, xclip).</summary>
internal sealed class DesktopClipboardProvider : IClipboardProvider
{
    public async Task<string> ReadText()
    {
        if (OperatingSystem.IsMacOS())
            return await RunProcessOutput("pbpaste", []);

        if (OperatingSystem.IsWindows())
            return await RunProcessOutput("powershell", ["-command", "Get-Clipboard"]);

        return await RunProcessOutput("xclip", ["-selection", "clipboard", "-o"]);
    }

    public async Task WriteText(string text)
    {
        if (OperatingSystem.IsMacOS())
            await RunProcessInput("pbcopy", [], text);
        else if (OperatingSystem.IsWindows())
            await RunProcessInput("clip", [], text);
        else
            await RunProcessInput("xclip", ["-selection", "clipboard"], text);
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
        foreach (var arg in args) psi.ArgumentList.Add(arg);

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
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
    }
}
