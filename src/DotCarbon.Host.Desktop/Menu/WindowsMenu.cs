namespace DotCarbon.Host.Desktop;

/// <summary>
/// Windows app-menu backend. The window handle is now plumbed through (Photino exposes the HWND), so
/// this will attach a Win32 HMENU via SetMenu and subclass the window proc to route WM_COMMAND to the
/// item handlers — landing as Task 2.1.
/// </summary>
internal static class WindowsMenu
{
    public static void Create(CarbonMenuBuilder builder, IntPtr window)
    {
        Console.Error.WriteLine(
            $"[Carbon] Native Windows menus are not wired yet ({builder.Groups.Count} top-level menu(s) configured).");
    }
}
