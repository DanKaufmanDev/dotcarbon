namespace DotCarbon.Plugins.Dialog;

public record OpenFileArgs(
    string Title = "Open File",
    string? DefaultPath = null,
    bool Multiple = false,
    string[]? Filters = null
);

public record SaveFileArgs(
    string Title = "Save File",
    string? DefaultPath = null,
    string? DefaultName = null,
    string[]? Filters = null
);

public record MessageArgs(
    string Title = "Message",
    string Message = "",
    string Kind = "info"
);

public record ConfirmArgs(
    string Title = "Confirm",
    string Message = ""
);

public record OpenFolderArgs(
    string Title = "Select Folder",
    string? DefaultPath = null
);