using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Windows application menu: a Win32 HMENU attached to Photino's window with SetMenu.
///
/// Photino owns the window procedure, so to receive menu clicks we subclass it
/// (SetWindowLongPtr/GWLP_WNDPROC), handle WM_COMMAND for our item ids, and chain everything else to
/// the original proc via CallWindowProc. Windows reserves space for the menu bar itself, so unlike
/// Linux/GTK no re-parenting of the webview is needed.
/// </summary>
internal static unsafe class WindowsMenu
{
    private const int GWLP_WNDPROC = -4;
    private const int WM_COMMAND = 0x0111;

    private const int MF_STRING = 0x0;
    private const int MF_POPUP = 0x10;
    private const int MF_SEPARATOR = 0x800;

    private static readonly Dictionary<int, Action> Handlers = new();
    private static IntPtr _originalProc;
    private static IntPtr _subclassProc; // keep the trampoline rooted

    public static void Create(CarbonMenuBuilder builder, IntPtr window)
    {
        try
        {
            var menuBar = CreateMenu();
            var id = 1; // 0 is reserved

            foreach (var group in builder.Groups)
            {
                var submenu = CreatePopupMenu();
                foreach (var item in group.Items)
                {
                    if (item.IsSeparator)
                    {
                        AppendMenuW(submenu, MF_SEPARATOR, IntPtr.Zero, null);
                        continue;
                    }
                    AppendMenuW(submenu, MF_STRING, id, item.Label);
                    Handlers[id] = item.OnClick!;
                    id++;
                }
                AppendMenuW(menuBar, MF_POPUP, submenu, group.Label);
            }

            if (!SetMenu(window, menuBar))
            {
                Console.Error.WriteLine("[Carbon] Native menu: SetMenu failed.");
                return;
            }
            DrawMenuBar(window);

            // Subclass once so WM_COMMAND reaches our handlers; everything else chains onward.
            if (_originalProc == IntPtr.Zero)
            {
                _subclassProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc;
                _originalProc = SetWindowLongPtrW(window, GWLP_WNDPROC, _subclassProc);
                if (_originalProc == IntPtr.Zero)
                    Console.Error.WriteLine("[Carbon] Native menu: could not subclass the window; clicks may not fire.");
            }

            Console.WriteLine($"[Carbon] Native menu ready ({builder.Groups.Count} top-level menu(s)).");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Windows menu: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Menu clicks arrive as WM_COMMAND with the item id in the low word of wParam and lParam 0
        // (lParam is non-zero for control notifications, which are not ours).
        if (msg == WM_COMMAND && lParam == IntPtr.Zero)
        {
            var id = (int)(wParam.ToInt64() & 0xFFFF);
            if (Handlers.TryGetValue(id, out var handler))
            {
                try { handler(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu handler failed: {ex.Message}"); }
                return IntPtr.Zero;
            }
        }
        return CallWindowProcW(_originalProc, hwnd, msg, wParam, lParam);
    }

    // --- Win32 interop -----------------------------------------------------------------------

    [DllImport("user32.dll")] private static extern IntPtr CreateMenu();
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, int idNewItem, string? newItem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, IntPtr idNewItem, string? newItem);
    [DllImport("user32.dll")] private static extern bool SetMenu(IntPtr hwnd, IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool DrawMenuBar(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr prevProc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
