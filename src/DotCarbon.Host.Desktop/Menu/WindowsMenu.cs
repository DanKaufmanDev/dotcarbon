namespace DotCarbon.Host.Desktop;

/// <summary>
/// Windows app-menu placeholder. Photino does not expose a native HWND during setup in a stable
/// public API; this backend keeps the host API cross-platform while validation decides whether to
/// attach a Win32 HMENU to the Photino window or move menu support behind a window-created hook.
/// </summary>
internal static class WindowsMenu
{
    public static void Create(CarbonMenuBuilder builder)
    {
        Console.Error.WriteLine(
            $"[Carbon] Native Windows menus are not wired yet ({builder.Groups.Count} top-level menu(s) configured).");
    }
}
