namespace DotCarbon.Plugins.Os;

public record OsInfo(
    string Platform,
    string Arch,
    string Version,
    string Family,
    string Hostname,
    string ExeExtension,
    string Eol);
