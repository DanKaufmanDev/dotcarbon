namespace DotCarbon.Plugins.FileSystem;
public record ReadFileArgs(string Path);
public record WriteFileArgs(string Path, string Contents);
public record ReadDirArgs(string Path);
public record RenameArgs(string OldPath, string NewPath);
public record DeleteArgs(string Path);
public record ExistsArgs(string Path);
public record DirEntry(
    string Name,
    string Path,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastModified
);