using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Shell;

[CarbonPlugin("Shell", description: "Run processes and open paths or URLs.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("shell:default", "Allow all shell commands.", Commands = new[] { "shell:*" })]
public partial class ShellPlugin : IPlugin
{
    private static readonly string[] DefaultDeniedEnv =
    [
        "NPM_TOKEN",
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "CARBON_UPDATER_PRIVATE_KEY",
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AZURE_CLIENT_SECRET",
        "GOOGLE_APPLICATION_CREDENTIALS"
    ];

    private ShellOptions _options = new();

    public string Namespace => "shell";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(ShellJsonContext.Default.ShellOptions);
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("execute")]
    public async Task<ShellResult> Execute(ExecuteArgs args)
    {
        // A sidecar is authorized by being bundled next to the app (bundle.externalBin), so it skips
        // the allowedPrograms check; a normal program must be on the allow-list.
        var fileName = args.Sidecar
            ? ResolveSidecar(args.Program, AppContext.BaseDirectory, Environment.CurrentDirectory)
            : args.Program;
        if (!args.Sidecar)
            EnsureProgramAllowed(args.Program);
        EnsureCwdAllowed(args.Cwd);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
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
            {
                EnsureEnvAllowed(key);
                psi.Environment[key] = value;
            }

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

    /// <summary>
    /// Resolves a sidecar name to an absolute executable path. A sidecar is a binary the developer
    /// bundled next to the app; the caller can only reach files physically present there, never an
    /// arbitrary path — the leaf name is used and any "<c>..</c>" is rejected, so it can't escape.
    /// Production: "&lt;appDir&gt;/&lt;leaf&gt;" (bundled beside the executable). Dev: "&lt;workingDir&gt;/&lt;name&gt;-&lt;triple&gt;".
    /// </summary>
    internal static string ResolveSidecar(string name, string appDir, string workingDir)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sidecar name cannot be empty.");
        if (name.Split('/', '\\').Contains(".."))
            throw new UnauthorizedAccessException($"Sidecar name cannot contain '..': {name}");

        var leaf = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(leaf))
            throw new ArgumentException($"Sidecar name has no file component: {name}");
        if (OperatingSystem.IsWindows() && !leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            leaf += ".exe";

        // Production: bundled beside the app executable.
        var bundled = Path.Combine(appDir, leaf);
        if (File.Exists(bundled))
            return bundled;

        // Dev (no bundle yet): the developer's "<name>-<triple>" binary, still under the project.
        var triple = SidecarTriple();
        var devName = name + "-" + triple + (OperatingSystem.IsWindows() ? ".exe" : "");
        var dev = Path.GetFullPath(Path.Combine(workingDir, devName));
        if (File.Exists(dev))
            return dev;

        throw new FileNotFoundException(
            $"Sidecar '{name}' not found. Looked beside the app ({bundled}) and in dev ({dev}). " +
            $"Declare it in bundle.externalBin and provide a '{name}-{triple}' binary.");
    }

    /// <summary>Rust-style target triple for the running platform, matching sidecar variant names.</summary>
    internal static string SidecarTriple()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x86_64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "i686",
            var other => other.ToString().ToLowerInvariant(),
        };
        if (OperatingSystem.IsMacOS()) return $"{arch}-apple-darwin";
        if (OperatingSystem.IsWindows()) return $"{arch}-pc-windows-msvc";
        return $"{arch}-unknown-linux-gnu";
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

    private void EnsureEnvAllowed(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Environment variable name cannot be empty.");

        var allowed = _options.AllowedEnv ?? [];
        if (allowed.Length > 0 && !allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Environment variable is not allowed: {key}");

        var denied = DefaultDeniedEnv.Concat(_options.DeniedEnv ?? []);
        if (denied.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Environment variable is denied: {key}");
    }
}
