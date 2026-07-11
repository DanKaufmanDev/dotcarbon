namespace DotCarbon.Plugins.GlobalShortcut;

public record RegisterShortcutArgs(string Id, string Accelerator, bool Suppress = false);

public record ShortcutIdArgs(string Id);

public record ShortcutInfo(string Id, string Accelerator, bool Suppress);

public record GlobalShortcutPressed(string Id, string Accelerator);
