using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Shell;

public partial class ShellPlugin : IPlugin
{
    public string Namespace => "shell";

    [CarbonCommand("execute")]
    public async Task<ShellResult> Execute(ExecuteArgs args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = args.Program,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (args.Args != null)
            foreach (var arg in args.Args)
                psi.ArgumentList.Add(arg);

        if (args.Cwd != null && Directory.Exists(args.Cwd))
            psi.WorkingDirectory = args.Cwd;

        if (args.Env != null)
            foreach (var (key, value) in args.Env)
                psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return new ShellResult(
            ExitCode: process.ExitCode,
            Stdout: stdout.ToString().TrimEnd(),
            Stderr: stderr.ToString().TrimEnd(),
            Success: process.ExitCode == 0
        );
    }

    [CarbonCommand("open")]
    public Task Open(OpenArgs args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = args.Path,
            UseShellExecute = true,
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }

    [CarbonCommand("open_url")]
    public Task OpenUrl(OpenArgs args)
    {
        var url = args.Path;

        if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        else
            Process.Start("xdg-open", url);

        return Task.CompletedTask;
    }
}