using DotCarbon.Plugins.Shell;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.7: sidecars are external binaries bundled next to the app. Resolution is the security
/// boundary — the caller can only reach a file physically bundled beside the executable, never an
/// arbitrary path — so these tests pin the resolved path, the "<c>..</c>" rejection, and a real run.
/// </summary>
public class ShellSidecarTests : IDisposable
{
    private readonly string _appDir;
    private readonly string _workingDir;

    public ShellSidecarTests()
    {
        _appDir = Directory.CreateTempSubdirectory("carbon-sidecar-app-").FullName;
        _workingDir = Directory.CreateTempSubdirectory("carbon-sidecar-cwd-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_appDir, recursive: true); } catch { /* best effort */ }
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best effort */ }
    }

    private static string Exe(string leaf) =>
        OperatingSystem.IsWindows() ? leaf + ".exe" : leaf;

    [Fact]
    public void Resolves_bundled_binary_beside_the_app()
    {
        // The "binaries/" prefix is dropped: the bundled file is just the leaf, next to the exe.
        var bundled = Path.Combine(_appDir, Exe("my-tool"));
        File.WriteAllText(bundled, "");

        var resolved = ShellPlugin.ResolveSidecar("binaries/my-tool", _appDir, _workingDir);

        Assert.Equal(bundled, resolved);
    }

    [Fact]
    public void Rejects_parent_directory_traversal()
    {
        // A name that tries to climb out with ".." is refused before any lookup.
        Assert.Throws<UnauthorizedAccessException>(
            () => ShellPlugin.ResolveSidecar("../../etc/passwd", _appDir, _workingDir));
    }

    [Fact]
    public void Falls_back_to_dev_triple_binary_under_working_dir()
    {
        // No bundle beside the app yet; in dev the "<name>-<triple>" binary under the project is used.
        var devName = "binaries/my-tool-" + ShellPlugin.SidecarTriple() + (OperatingSystem.IsWindows() ? ".exe" : "");
        var dev = Path.Combine(_workingDir, devName);
        Directory.CreateDirectory(Path.GetDirectoryName(dev)!);
        File.WriteAllText(dev, "");

        var resolved = ShellPlugin.ResolveSidecar("binaries/my-tool", _appDir, _workingDir);

        Assert.Equal(Path.GetFullPath(dev), resolved);
    }

    [Fact]
    public void Missing_sidecar_throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => ShellPlugin.ResolveSidecar("binaries/nope", _appDir, _workingDir));
    }

    [Fact]
    public async Task Executes_a_bundled_sidecar_and_captures_stdout()
    {
        // A sidecar runs without allowedPrograms because bundling it beside the app is the authorization.
        // Windows batch invocation is a separate harness concern; the resolution path is covered above.
        if (OperatingSystem.IsWindows()) return;

        // Place the script beside the running test assembly — exactly where a bundled sidecar lives.
        var leaf = "carbon-sidecar-" + Guid.NewGuid().ToString("N");
        var script = Path.Combine(AppContext.BaseDirectory, leaf);
        await File.WriteAllTextAsync(script, "#!/bin/sh\necho carbon-sidecar-ok\n");
        File.SetUnixFileMode(script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        try
        {
            var result = await new ShellPlugin().Execute(new ExecuteArgs(leaf, Sidecar: true));

            Assert.True(result.Success, result.Stderr);
            Assert.Equal("carbon-sidecar-ok", result.Stdout);
        }
        finally { File.Delete(script); }
    }
}
