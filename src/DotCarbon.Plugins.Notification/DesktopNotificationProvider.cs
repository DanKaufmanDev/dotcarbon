using System.Diagnostics;

namespace DotCarbon.Plugins.Notification;

/// <summary>Desktop notifications via the platform tools (osascript, PowerShell toast, notify-send).</summary>
internal sealed class DesktopNotificationProvider : INotificationProvider
{
    public async Task Send(SendNotificationArgs args)
    {
        if (OperatingSystem.IsMacOS())
            await SendMacOS(args);
        else if (OperatingSystem.IsWindows())
            await SendWindows(args);
        else
            await SendLinux(args);
    }

    private static async Task SendMacOS(SendNotificationArgs args)
    {
        var subtitle = args.Subtitle != null ? $"with subtitle \"{Escape(args.Subtitle)}\"" : "";
        var script = $"display notification \"{Escape(args.Body)}\" " +
                     $"with title \"{Escape(args.Title)}\" {subtitle}";
        await RunProcess("osascript", ["-e", script]);
    }

    private static async Task SendWindows(SendNotificationArgs args)
    {
        var script = $@"
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            $template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
            $textNodes = $template.GetElementsByTagName('text')
            $textNodes.Item(0).AppendChild($template.CreateTextNode('{Escape(args.Title)}')) | Out-Null
            $textNodes.Item(1).AppendChild($template.CreateTextNode('{Escape(args.Body)}')) | Out-Null
            $toast = [Windows.UI.Notifications.ToastNotification]::new($template)
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Carbon').Show($toast)
        ";
        await RunProcess("powershell", ["-Command", script]);
    }

    private static async Task SendLinux(SendNotificationArgs args) =>
        await RunProcess("notify-send", [args.Title, args.Body]);

    private static async Task RunProcess(string program, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
