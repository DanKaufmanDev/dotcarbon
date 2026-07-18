using System.Diagnostics;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Plugins.Process;

/// <summary>
/// Exit and relaunch the app (Task 4.4). Desktop only — mobile apps are managed by the OS and cannot
/// meaningfully exit or restart themselves.
/// </summary>
[CarbonPlugin("Process", description: "Exit and relaunch the application.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("process:default", "Allow all process commands.", Commands = new[] { "process:*" })]
public partial class ProcessPlugin : IPlugin
{
    public string Namespace => "process";

    /// <summary>The current process id.</summary>
    [CarbonCommand("pid")]
    public int Pid() => Environment.ProcessId;

    /// <summary>Exit the app immediately with the given code.</summary>
    [CarbonCommand("exit")]
    public void Exit(ExitArgs args) => Environment.Exit(args.Code);

    /// <summary>Start a fresh copy of the app with the same arguments, then exit this one.</summary>
    [CarbonCommand("relaunch")]
    public void Relaunch()
    {
        if (Environment.ProcessPath is { } executable)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
                start.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(start);
        }
        Environment.Exit(0);
    }
}
