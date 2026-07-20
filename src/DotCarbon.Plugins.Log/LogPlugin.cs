using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Log;

/// <summary>
/// Leveled logging from the frontend and C# (Task 6.1). Records go to any configured targets — stdout,
/// a rolling file in the app's log directory, and the webview console (via the <c>log:message</c> event
/// that <c>attachConsole</c> subscribes to). Levels below the configured minimum are dropped.
/// </summary>
[CarbonPlugin("Log", description: "Leveled logging to stdout, a rolling file, and the webview console.")]
[CarbonPermission("log:default", "Allow all log commands.", Commands = new[] { "log:*" })]
[CarbonEvent("log:message", "LogRecord", "A log record, for the webview console (attachConsole).")]
public partial class LogPlugin : IPlugin
{
    private static readonly string[] Levels = ["trace", "debug", "info", "warn", "error"];

    private readonly object _fileLock = new();
    private AppHandle _app = default!;
    private LogOptions _options = new();

    public string Namespace => "log";

    public ValueTask InitializeAsync(PluginContext context)
    {
        _app = context.App;
        if (context.HasConfiguration)
            _options = context.GetConfiguration(LogJsonContext.Default.LogOptions);
        return ValueTask.CompletedTask;
    }

    [CarbonCommand("log")]
    public Task Log(LogArgs args) => WriteAsync(args.Level, args.Message, args.Location);

    // Convenience so C# code holding the plugin can log at each level.
    public Task Trace(string message) => WriteAsync("trace", message, null);
    public Task Debug(string message) => WriteAsync("debug", message, null);
    public Task Info(string message) => WriteAsync("info", message, null);
    public Task Warn(string message) => WriteAsync("warn", message, null);
    public Task Error(string message) => WriteAsync("error", message, null);

    internal async Task WriteAsync(string level, string message, string? location)
    {
        if (LevelIndex(level) < LevelIndex(_options.Level ?? "info"))
            return;

        var record = new LogRecord(Levels[LevelIndex(level)], message, DateTimeOffset.Now.ToString("O"), location);
        var targets = _options.Targets ?? ["stdout", "webview"];

        if (Enabled(targets, "stdout"))
            Console.WriteLine(Format(record));

        if (Enabled(targets, "file"))
            AppendToFile(Format(record));

        if (Enabled(targets, "webview"))
            await _app.EmitAsync(new CarbonEventName<LogRecord>("log:message"), record, LogJsonContext.Default.LogRecord);
    }

    private static bool Enabled(string[] targets, string target) =>
        targets.Contains(target, StringComparer.OrdinalIgnoreCase);

    private static string Format(LogRecord record) =>
        $"[{record.Timestamp}] [{record.Level.ToUpperInvariant()}]" +
        $"{(record.Location is null ? string.Empty : $" [{record.Location}]")} {record.Message}";

    private void AppendToFile(string line)
    {
        var path = LogFilePath();
        lock (_fileLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var max = _options.MaxFileBytes > 0 ? _options.MaxFileBytes : 5 * 1024 * 1024;
            if (File.Exists(path) && new FileInfo(path).Length >= max)
            {
                var rolled = path + ".1";
                if (File.Exists(rolled)) File.Delete(rolled);
                File.Move(path, rolled);
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    internal string LogFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.File))
            return Path.GetFullPath(_options.File);

        var appId = _app.Config.App.Identifier;
        var name = string.IsNullOrWhiteSpace(_app.Config.App.Name) ? "app" : _app.Config.App.Name;
        var dir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", appId)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appId, "logs");
        return Path.Combine(dir, name + ".log");
    }

    // Unknown levels resolve to "info" so a typo logs rather than vanishing.
    private static int LevelIndex(string level)
    {
        var index = Array.FindIndex(Levels, item => item.Equals(level, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 2;
    }
}
