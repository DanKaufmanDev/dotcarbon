using System.CommandLine;
using System.Diagnostics;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class DevCommand
{
    public static Command Build()
    {
        var command = new Command("dev", "Start Carbon in development mode");
        var projectOption = new Option<DirectoryInfo?>(
            name: "--project",
            description: "Path to the Carbon project (default: current directory)"
        );
        var noTypesOption = new Option<bool>(
            name: "--no-types",
            description: "Do not generate ui/src/carbon.d.ts while dev mode is running"
        );
        var typesOutOption = new Option<FileInfo?>(
            name: "--types-out",
            description: "Output path for generated TypeScript declarations (default: ui/src/carbon.d.ts)"
        );
        var noCapabilitiesOption = new Option<bool>(
            name: "--no-capabilities",
            description: "Do not sync discovered commands into src-carbon/capabilities/main.json"
        );

        command.AddOption(projectOption);
        command.AddOption(noTypesOption);
        command.AddOption(typesOutOption);
        command.AddOption(noCapabilitiesOption);
        command.SetHandler(Run, projectOption, noTypesOption, typesOutOption, noCapabilitiesOption);

        command.AddCommand(AndroidSubcommand());

        return command;
    }

    private static Command AndroidSubcommand()
    {
        var cmd = new Command("android", "Build and run the Android app on a device or emulator");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        cmd.AddOption(project);
        cmd.SetHandler(async context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Carbon] No carbon.json found in {workingDir}");
                Console.ResetColor();
                context.ExitCode = 1;
                return;
            }
            var config = ConfigLoader.Load(configPath);
            context.ExitCode = await new Bundling.AndroidBundler().DevAsync(config, workingDir);
        });
        return cmd;
    }

    private static async Task Run(DirectoryInfo? projectDir, bool noTypes, FileInfo? typesOut, bool noCapabilities)
    {
        var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");

        if (!File.Exists(configPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Carbon] No carbon.json found in {workingDir}");
            Console.ResetColor();
            return;
        }

        var config = ConfigLoader.Load(configPath);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚡ Carbon dev mode starting...");
        Console.ResetColor();

        using var cts = new CancellationTokenSource();
        using var typeWatcher = noTypes
            ? null
            : StartTypesGeneration(workingDir, typesOut?.FullName, !noCapabilities, cts.Token);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Carbon] Shutting down...");
            cts.Cancel();
        };
        await Task.WhenAny(
            RunFrontend(config, workingDir, cts.Token),
            RunHost(workingDir, cts.Token)
        );

        cts.Cancel();
        Console.WriteLine("[Carbon] Done.");
    }

    private static IDisposable? StartTypesGeneration(
        string workingDir,
        string? outPath,
        bool syncCapabilities,
        CancellationToken ct)
    {
        GenerateTypes(workingDir, outPath, syncCapabilities);

        var carbonDir = Path.Combine(workingDir, "src-carbon");
        var watchDir = Directory.Exists(carbonDir) ? carbonDir : workingDir;
        var watcher = new FileSystemWatcher(watchDir, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        var debounce = new DebouncedAction(TimeSpan.FromMilliseconds(250), ct, () =>
            GenerateTypes(workingDir, outPath, syncCapabilities));

        FileSystemEventHandler onChange = (_, e) =>
        {
            if (IsIgnoredPath(e.FullPath)) return;
            debounce.Schedule();
        };
        RenamedEventHandler onRename = (_, e) =>
        {
            if (IsIgnoredPath(e.FullPath)) return;
            debounce.Schedule();
        };

        watcher.Created += onChange;
        watcher.Changed += onChange;
        watcher.Deleted += onChange;
        watcher.Renamed += onRename;

        Console.WriteLine($"[Carbon] Watching C# commands for type generation: {watchDir}");
        return new CompositeDisposable(watcher, debounce);
    }

    private static void GenerateTypes(string workingDir, string? outPath, bool syncCapabilities)
    {
        try
        {
            var result = TypesCommand.Generate(workingDir, outPath, syncCapabilities);
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("[Types] ");
            Console.ResetColor();
            Console.WriteLine($"Generated {result.CommandCount} command type(s) -> {result.TargetPath}");
            if (result.SyncedCapabilityCount > 0 && result.CapabilityPath is not null)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("[Types] ");
                Console.ResetColor();
                Console.WriteLine($"Synced {result.SyncedCapabilityCount} command(s) -> {result.CapabilityPath}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[Types] ");
            Console.ResetColor();
            Console.WriteLine($"Type generation skipped: {ex.Message}");
        }
    }

    private static async Task RunFrontend(CarbonConfig config, string workingDir, CancellationToken ct)
    {
        var parts = config.Build.DevCommand.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : string.Empty;
        var frontendDir = Path.GetFullPath(
            Path.Combine(workingDir, Path.GetDirectoryName(config.Build.FrontendDist) ?? "ui")
        );

        var packageJsonDir = FindPackageJson(frontendDir) ?? workingDir;

        Console.WriteLine($"[Carbon] Starting frontend: {config.Build.DevCommand}");
        Console.WriteLine($"[Carbon] Frontend dir: {packageJsonDir}");

        await RunProcess(command, args, packageJsonDir, "[UI]", ConsoleColor.Green, ct);
    }

    private static async Task RunHost(string workingDir, CancellationToken ct)
    {
        await Task.Delay(2000, ct);

        Console.WriteLine("[Carbon] Starting .NET host...");

        var config = ConfigLoader.Load(Path.Combine(workingDir, "carbon.json"));
        var hostProject = ProjectLocator.FindHostProject(workingDir, config);

        if (hostProject is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Carbon] Could not find host .csproj. Is there a project referencing DotCarbon.Core?");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"[Carbon] Host project: {hostProject}");

        await RunProcess(
            "dotnet", $"run --project \"{hostProject}\"",
            workingDir, "[C#]", ConsoleColor.Magenta, ct
        );
    }

    private static async Task RunProcess(
        string command,
        string args,
        string workingDir,
        string prefix,
        ConsoleColor color,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.ForegroundColor = color;
            Console.Write($"{prefix} ");
            Console.ResetColor();
            Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{prefix} ");
            Console.ResetColor();
            Console.WriteLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{prefix} Failed to start: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static string? FindPackageJson(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static bool IsIgnoredPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part is "bin" or "obj" or "node_modules" or "out");
    }

    private sealed class DebouncedAction : IDisposable
    {
        private readonly TimeSpan _delay;
        private readonly CancellationToken _ct;
        private readonly Action _action;
        private readonly object _lock = new();
        private CancellationTokenSource? _pending;

        public DebouncedAction(TimeSpan delay, CancellationToken ct, Action action)
        {
            _delay = delay;
            _ct = ct;
            _action = action;
        }

        public void Schedule()
        {
            lock (_lock)
            {
                _pending?.Cancel();
                _pending?.Dispose();
                _pending = CancellationTokenSource.CreateLinkedTokenSource(_ct);
                _ = RunAsync(_pending.Token);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _pending?.Cancel();
                _pending?.Dispose();
                _pending = null;
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(_delay, ct);
                _action();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;

        public CompositeDisposable(params IDisposable[] items) => _items = items;

        public void Dispose()
        {
            foreach (var item in _items)
                item.Dispose();
        }
    }
}
