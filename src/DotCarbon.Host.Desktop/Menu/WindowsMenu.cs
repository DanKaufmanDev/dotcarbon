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

    private const int MF_BYCOMMAND = 0x0;
    private const int MF_ENABLED = 0x0;
    private const int MF_GRAYED = 0x1;
    private const int MF_CHECKED = 0x8;
    private const int MF_UNCHECKED = 0x0;
    private const int MIIM_STRING = 0x40;

    private static readonly Dictionary<int, Action> Handlers = new();
    private static readonly Dictionary<string, int> CommandsById = new(StringComparer.Ordinal);
    private static IntPtr _originalProc;
    private static IntPtr _subclassProc; // keep the trampoline rooted
    private static IntPtr _menuBar;
    private static IntPtr _window;

    // --- runtime mutation (Task 2.4) ---------------------------------------------------------
    // Win32 addresses menu items by command id, so the id->command map is all we need.

    public static void SetEnabled(string id, bool enabled)
    {
        if (!TryCommand(id, out var command)) return;
        EnableMenuItem(_menuBar, command, MF_BYCOMMAND | (enabled ? MF_ENABLED : MF_GRAYED));
        DrawMenuBar(_window);
    }

    public static void SetChecked(string id, bool isChecked)
    {
        if (!TryCommand(id, out var command)) return;
        CheckMenuItem(_menuBar, command, MF_BYCOMMAND | (isChecked ? MF_CHECKED : MF_UNCHECKED));
    }

    public static void SetLabel(string id, string label)
    {
        if (!TryCommand(id, out var command)) return;
        var info = new MENUITEMINFOW
        {
            cbSize = (uint)sizeof(MENUITEMINFOW),
            fMask = MIIM_STRING,
            dwTypeData = Marshal.StringToHGlobalUni(label),
        };
        try
        {
            SetMenuItemInfoW(_menuBar, command, fByPosition: false, ref info);
            DrawMenuBar(_window);
        }
        finally
        {
            Marshal.FreeHGlobal(info.dwTypeData);
        }
    }

    private static bool TryCommand(string id, out int command)
    {
        command = 0;
        if (_menuBar == IntPtr.Zero) return false;
        return CommandsById.TryGetValue(id, out command);
    }

    public static void Create(CarbonMenuBuilder builder, IntPtr window, Action<CarbonMenuHandle>? onReady = null)
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
                    if (item.IsCheckItem && item.IsChecked)
                        CheckMenuItem(submenu, id, MF_BYCOMMAND | MF_CHECKED);
                    if (item.Id is { } itemId)
                        CommandsById[itemId] = id;
                    id++;
                }
                AppendMenuW(menuBar, MF_POPUP, submenu, group.Label);
            }

            if (!SetMenu(window, menuBar))
            {
                Console.Error.WriteLine("[Carbon] Native menu: SetMenu failed.");
                return;
            }
            _menuBar = menuBar;
            _window = window;
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
            CarbonMenu.NotifyReady(onReady);
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
    [DllImport("user32.dll")] private static extern bool EnableMenuItem(IntPtr hMenu, int idEnableItem, int enable);
    [DllImport("user32.dll")] private static extern bool CheckMenuItem(IntPtr hMenu, int idCheckItem, int check);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetMenuItemInfoW(IntPtr hMenu, int item,
        [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr prevProc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
