using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Windows system tray implemented with Shell_NotifyIcon and a hidden callback window.
/// </summary>
internal static unsafe class WindowsTray
{
    private const int WM_APP = 0x8000;
    private const int CallbackMessage = WM_APP + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_DESTROY = 0x0002;

    private const int NIM_ADD = 0x0;
    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;

    private const int MF_STRING = 0x0;
    private const int MF_SEPARATOR = 0x800;
    private const int TPM_RIGHTBUTTON = 0x2;
    private const int TPM_RETURNCMD = 0x100;
    private const int IDI_APPLICATION = 32512;

    private static readonly Dictionary<int, Action> Handlers = new();
    private static IntPtr _hwnd;
    private static IntPtr _menu;
    private static IntPtr _wndProcPtr; // keep the delegate ptr rooted

    public static void Create(CarbonTrayBuilder builder)
    {
        try
        {
            var hInstance = GetModuleHandleW(IntPtr.Zero);
            var className = Marshal.StringToHGlobalUni("CarbonTrayWindow");
            _wndProcPtr = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc;

            var wc = new WNDCLASSEXW
            {
                cbSize = sizeof(WNDCLASSEXW),
                lpfnWndProc = _wndProcPtr,
                hInstance = hInstance,
                lpszClassName = className,
            };
            RegisterClassExW(ref wc);

            _hwnd = CreateWindowExW(0, className, IntPtr.Zero, 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            _menu = CreatePopupMenu();
            var id = 1; // TPM_RETURNCMD reserves 0 for "no selection"
            foreach (var item in builder.Items)
            {
                if (item.IsSeparator)
                {
                    AppendMenuW(_menu, MF_SEPARATOR, IntPtr.Zero, null);
                }
                else
                {
                    AppendMenuW(_menu, MF_STRING, id, item.Label);
                    Handlers[id] = item.OnClick!;
                    id++;
                }
            }

            var data = new NOTIFYICONDATA
            {
                cbSize = sizeof(NOTIFYICONDATA),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = CallbackMessage,
                hIcon = LoadIconW(IntPtr.Zero, IDI_APPLICATION),
            };
            CopyTip(ref data, builder.Title);

            if (!Shell_NotifyIconW(NIM_ADD, ref data))
                Console.Error.WriteLine("[Carbon] Tray: Shell_NotifyIcon(NIM_ADD) failed.");
            else
                Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Windows tray: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            var mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is WM_RBUTTONUP or WM_LBUTTONUP)
                ShowMenu(hwnd);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void ShowMenu(IntPtr hwnd)
    {
        GetCursorPos(out var point);
        SetForegroundWindow(hwnd); // required so the menu dismisses correctly
        var chosen = TrackPopupMenu(_menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, point.X, point.Y, 0, hwnd, IntPtr.Zero);
        if (chosen != 0 && Handlers.TryGetValue(chosen, out var handler))
        {
            try { handler(); }
            catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray handler failed: {ex.Message}"); }
        }
    }

    private static void CopyTip(ref NOTIFYICONDATA data, string tip)
    {
        var text = tip.Length > 127 ? tip[..127] : tip;
        for (var i = 0; i < text.Length; i++) data.szTip[i] = text[i];
        data.szTip[text.Length] = '\0';
    }

    // Win32 interop

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public int cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public int dwState, dwStateMask;
        public fixed char szInfo[256];
        public int uVersionOrTimeout;
        public fixed char szInfoTitle[64];
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);
    [DllImport("user32.dll")] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, IntPtr className, IntPtr windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, int idNewItem, string? newItem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, IntPtr idNewItem, string? newItem);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr hMenu, int flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("shell32.dll")] private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATA data);
}
