using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Shell;

[CarbonPlugin("Shell", description: "Run processes and open paths or URLs.")]
[CarbonPermission("shell:default", "Allow all shell commands.", Commands = new[] { "shell:*" })]
public partial class ShellPlugin : IPlugin
{
    private ShellOptions _options = new();

    public string Namespace => "shell";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration<ShellOptions>();
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("execute")]
    public async Task<ShellResult> Execute(ExecuteArgs args)
    {
        EnsureProgramAllowed(args.Program);
        EnsureCwdAllowed(args.Cwd);

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
        if (!_options.AllowOpenPaths)
            throw new UnauthorizedAccessException("shell:open requires plugins.shell.allowOpenPaths = true.");

        var path = Path.GetFullPath(args.Path);
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"Path does not exist: {path}");

        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }

    [CarbonCommand("open_url")]
    public Task OpenUrl(OpenArgs args)
    {
        var url = args.Path;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL must be absolute.");
        var allowedSchemes = _options.AllowedUrlSchemes ?? ["http", "https", "mailto"];
        if (!allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"URL scheme is not allowed: {uri.Scheme}");

        if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        else
            Process.Start("xdg-open", url);

        return Task.CompletedTask;
    }

    private void EnsureProgramAllowed(string program)
    {
        var allowed = _options.AllowedPrograms ?? [];
        if (allowed.Length == 0)
            throw new UnauthorizedAccessException(
                "shell:execute requires plugins.shell.allowedPrograms to include the requested program.");

        var requested = Path.GetFileName(program);
        if (!allowed.Any(item =>
            item.Equals(program, StringComparison.OrdinalIgnoreCase) ||
            item.Equals(requested, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Program is not allowed: {program}");
    }

    private void EnsureCwdAllowed(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return;

        var allowed = _options.AllowedCwds ?? [];
        if (allowed.Length == 0)
            throw new UnauthorizedAccessException("Working directories are not allowed by shell configuration.");

        var requested = Path.GetFullPath(cwd);
        if (!allowed.Select(Path.GetFullPath).Any(root =>
            requested.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            requested.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException($"Working directory is not allowed: {cwd}");
    }
}
