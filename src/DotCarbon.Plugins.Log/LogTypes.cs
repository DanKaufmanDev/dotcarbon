namespace DotCarbon.Plugins.Log;

/// <summary>A log call from the frontend (or C#): a level, a message, and an optional source location.</summary>
public record LogArgs(string Level, string Message, string? Location = null);

/// <summary>A formatted log record delivered to the webview console via the <c>log:message</c> event.</summary>
public record LogRecord(string Level, string Message, string Timestamp, string? Location);

/// <summary>
/// Plugin configuration (<c>plugins.log</c>). <c>Targets</c> is any of "stdout", "file", "webview"
/// (default stdout + webview); <c>Level</c> is the minimum level written; <c>File</c> overrides the log
/// path (default a rolling file in the app's log directory).
/// </summary>
public record LogOptions(
    string[]? Targets = null,
    string? Level = null,
    string? File = null,
    long MaxFileBytes = 5 * 1024 * 1024
);
