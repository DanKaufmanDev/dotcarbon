using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.FileSystem;

public partial class FileSystemPlugin : IPlugin
{
    public string Namespace => "fs";

    [CarbonCommand("read_file")]
    public async Task<string> ReadFile(ReadFileArgs args)
    {
        EnsureSafe(args.Path);
        return await File.ReadAllTextAsync(args.Path);
    }

    [CarbonCommand("write_file")]
    public async Task WriteFile(WriteFileArgs args)
    {
        EnsureSafe(args.Path);

        var dir = Path.GetDirectoryName(args.Path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(args.Path, args.Contents);
    }

    [CarbonCommand("read_dir")]
    public Task<DirEntry[]> ReadDir(ReadDirArgs args)
    {
        EnsureSafe(args.Path);

        if (!Directory.Exists(args.Path))
            throw new DirectoryNotFoundException($"Directory not found: {args.Path}");

        var entries = Directory.GetFileSystemEntries(args.Path)
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
        EnsureSafe(args.Path);
        return Task.FromResult(File.Exists(args.Path) || Directory.Exists(args.Path));
    }

    [CarbonCommand("rename")]
    public Task Rename(RenameArgs args)
    {
        EnsureSafe(args.OldPath);
        EnsureSafe(args.NewPath);
        File.Move(args.OldPath, args.NewPath);
        return Task.CompletedTask;
    }

    [CarbonCommand("delete")]
    public Task Delete(DeleteArgs args)
    {
        EnsureSafe(args.Path);

        if (Directory.Exists(args.Path))
            Directory.Delete(args.Path, recursive: true);
        else if (File.Exists(args.Path))
            File.Delete(args.Path);
        else
            throw new FileNotFoundException($"Not found: {args.Path}");

        return Task.CompletedTask;
    }

    [CarbonCommand("create_dir")]
    public Task CreateDir(ReadDirArgs args)
    {
        EnsureSafe(args.Path);
        Directory.CreateDirectory(args.Path);
        return Task.CompletedTask;
    }

    private static void EnsureSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty");

        var fullPath = Path.GetFullPath(path);

        if (path.Contains(".."))
            throw new UnauthorizedAccessException($"Path traversal not allowed: {path}");
    }
}