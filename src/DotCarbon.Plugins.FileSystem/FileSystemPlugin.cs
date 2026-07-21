using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.FileSystem;

[CarbonPlugin("File system", description: "Read and write files and directories.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("fs:default", "Allow all file-system commands.", Commands = new[] { "fs:*" })]
public partial class FileSystemPlugin : IPlugin
{
    private readonly AppHandle _app;
    private FileSystemOptions _options = new([]);

    public FileSystemPlugin(AppHandle app)
    {
        _app = app;
    }

    public string Namespace => "fs";

    public ValueTask InitializeAsync(PluginContext context)
    {
        if (context.HasConfiguration)
            _options = context.GetConfiguration(FileSystemJsonContext.Default.FileSystemOptions) ?? new([]);
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("read_file")]
    public async Task<string> ReadFile(ReadFileArgs args)
    {
        var path = EnsureAllowed(args.Path);
        return await File.ReadAllTextAsync(path);
    }

    [CarbonCommand("write_file")]
    public async Task WriteFile(WriteFileArgs args)
    {
        var path = EnsureAllowed(args.Path);

        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, args.Contents);
    }

    [CarbonCommand("read_dir")]
    public Task<DirEntry[]> ReadDir(ReadDirArgs args)
    {
        var path = EnsureAllowed(args.Path);

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var entries = Directory.GetFileSystemEntries(path)
            .Select(entryPath =>
            {
                var isDir = Directory.Exists(entryPath);
                var info = isDir
                    ? (FileSystemInfo)new DirectoryInfo(entryPath)
                    : new FileInfo(entryPath);

                return new DirEntry(
                    Name: Path.GetFileName(entryPath),
                    Path: entryPath,
                    IsDirectory: isDir,
                    Size: isDir ? 0 : ((FileInfo)info).Length,
                    LastModified: info.LastWriteTime
                );
            })
            .OrderBy(e => !e.IsDirectory)
            .ThenBy(e => e.Name)
            .ToArray();

        return Task.FromResult(entries);
    }

    [CarbonCommand("exists")]
    public Task<bool> Exists(ExistsArgs args)
    {
        var path = EnsureAllowed(args.Path);
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    [CarbonCommand("rename")]
    public Task Rename(RenameArgs args)
    {
        var oldPath = EnsureAllowed(args.OldPath);
        var newPath = EnsureAllowed(args.NewPath);
        File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    [CarbonCommand("delete")]
    public Task Delete(DeleteArgs args)
    {
        var path = EnsureAllowed(args.Path);

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
        else
            throw new FileNotFoundException($"Not found: {path}");

        return Task.CompletedTask;
    }

    [CarbonCommand("create_dir")]
    public Task CreateDir(ReadDirArgs args)
    {
        var path = EnsureAllowed(args.Path);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    private string EnsureAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty");

        var fullPath = Path.GetFullPath(path);

        // The static plugin config scopes and the per-capability scopes granted to the current window
        // (Task 5.3) are merged: either may allow a path; a capability deny always wins.
        var scope = _app.ResolvePermissionScope(_app.CurrentWindow, Namespace);
        var allowRoots = (_options.Scopes ?? [])
            .Concat(scope.Allow)
            .Select(ResolveScopeRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .ToArray();

        // Roots granted at runtime (Task 6.9) — e.g. a folder the user picked — count as allowed too.
        var runtimeAllows = CarbonRuntimeScope.HasEntries(CarbonRuntimeScope.FileSystem);

        if (allowRoots.Length == 0 && !runtimeAllows)
            throw new UnauthorizedAccessException(
                "File-system access requires an allowed scope (plugins.fs.scopes or a capability fs permission allow).");

        if (!allowRoots.Any(root => IsWithin(fullPath, root)) &&
            !CarbonRuntimeScope.IsAllowed(CarbonRuntimeScope.FileSystem, fullPath))
            throw new UnauthorizedAccessException($"Path is outside the allowed file-system scopes: {path}");

        if (scope.Deny
            .Select(ResolveScopeRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => IsWithin(fullPath, root)))
            throw new UnauthorizedAccessException($"Path is denied by a capability scope: {path}");

        return fullPath;
    }

    private static string ResolveScopeRoot(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return string.Empty;

        // A trailing glob ("$APPDATA/*", "logs/**") just means "everything under it" — containment via
        // IsWithin already covers that — so resolve to the last non-glob directory segment.
        var value = TrimGlobTail(scope.Trim());
        if (value.Length == 0) return string.Empty;

        string root;
        if (value.StartsWith('$'))
        {
            var separator = value.IndexOfAny(['/', '\\']);
            var token = (separator < 0 ? value[1..] : value[1..separator]).ToLowerInvariant();
            var rest = separator < 0 ? string.Empty : value[(separator + 1)..];
            var baseDir = token switch
            {
                "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "temp" or "tmp" => Path.GetTempPath(),
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(baseDir)) return string.Empty;
            root = string.IsNullOrEmpty(rest) ? baseDir : Path.Combine(baseDir, rest);
        }
        else
        {
            root = ExpandHome(value);
        }

        return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.GetFullPath(root);
    }

    private static string TrimGlobTail(string value)
    {
        var parts = value.Split('/', '\\');
        var kept = parts.TakeWhile(part => !part.Contains('*') && !part.Contains('?')).ToArray();
        return kept.Length == parts.Length ? value : string.Join(Path.DirectorySeparatorChar, kept);
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            path.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);

        return path;
    }

    private static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return path.Equals(normalizedRoot, comparison) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison) ||
               path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison);
    }
}
