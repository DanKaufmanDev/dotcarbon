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

    // Notify-icon mouse messages (Task 2.8).
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONDBLCLK = 0x0206;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MBUTTONDBLCLK = 0x0209;

    /// <summary>Shell_NotifyIcon identifies our icon by (hWnd, uID).</summary>
    private const int TrayIconId = 1;

    private const int NIM_ADD = 0x0;
    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;

    private const int MF_STRING = 0x0;
    private const int MF_POPUP = 0x10;
    private const int MF_SEPARATOR = 0x800;
    private const int TPM_RIGHTBUTTON = 0x2;
    private const int TPM_RETURNCMD = 0x100;
    private const int IDI_APPLICATION = 32512;

    private static readonly Dictionary<int, Action> Handlers = new();
    private static IntPtr _hwnd;
    private static IntPtr _menu;
    private static IntPtr _wndProcPtr; // keep the delegate ptr rooted
    private static Action<CarbonTrayEvent>? _onEvent;

    // --- runtime mutation (Task 2.3) ---------------------------------------------------------
    // The Windows tray is icon-only, so SetTitle has no analogue here (see CarbonTrayHandle).
    // Shell_NotifyIcon identifies the icon by (hWnd, uID), so updates just re-send the struct.

    private const int NIM_MODIFY = 0x1;
    private const int NIM_DELETE = 0x2;

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x0010;
    private const int LR_DEFAULTSIZE = 0x0040;

    private static string _tip = string.Empty;
    private static bool _created;
    private static IntPtr _icon;          // custom icon, if any
    private static ulong _gdiplusToken;

    public static void SetIcon(string path)
    {
        var icon = LoadIcon(path);
        if (icon == IntPtr.Zero) return;
        var previous = _icon;
        _icon = icon;
        if (_created) Notify(NIM_MODIFY);
        if (previous != IntPtr.Zero) DestroyIcon(previous);
    }

    /// <summary>
    /// LoadImage only understands .ico, so anything else (typically a PNG) is decoded through GDI+
    /// and converted to an HICON. Returns IntPtr.Zero on failure, leaving the current icon alone.
    /// </summary>
    private static IntPtr LoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[Carbon] Tray: icon file not found: {path}");
                return IntPtr.Zero;
            }

            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var handle = LoadImageW(IntPtr.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (handle == IntPtr.Zero) Console.Error.WriteLine($"[Carbon] Tray: could not load icon: {path}");
                return handle;
            }

            if (_gdiplusToken == 0)
            {
                var input = new GdiplusStartupInput { GdiplusVersion = 1 };
                if (GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero) != 0)
                {
                    Console.Error.WriteLine("[Carbon] Tray: GDI+ startup failed; use an .ico icon.");
                    return IntPtr.Zero;
                }
            }

            if (GdipCreateBitmapFromFile(path, out var bitmap) != 0 || bitmap == IntPtr.Zero)
            {
                Console.Error.WriteLine($"[Carbon] Tray: could not decode icon: {path}");
                return IntPtr.Zero;
            }
            try
            {
                return GdipCreateHICONFromBitmap(bitmap, out var hicon) == 0 ? hicon : IntPtr.Zero;
            }
            finally
            {
                GdipDisposeImage(bitmap);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray: icon load failed: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    public static void SetTooltip(string tooltip)
    {
        _tip = tooltip;
        if (_created) Notify(NIM_MODIFY);
    }

    public static void SetVisible(bool visible)
    {
        if (visible == _created) return;
        if (Notify(visible ? NIM_ADD : NIM_DELETE)) _created = visible;
    }

    public static void Remove()
    {
        if (!_created) return;
        if (Notify(NIM_DELETE)) _created = false;
    }

    private static bool Notify(int message)
    {
        try
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = sizeof(NOTIFYICONDATA),
                hWnd = _hwnd,
                uID = TrayIconId,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = CallbackMessage,
                hIcon = _icon != IntPtr.Zero ? _icon : LoadIconW(IntPtr.Zero, IDI_APPLICATION),
            };
            CopyTip(ref data, _tip);
            if (Shell_NotifyIconW(message, ref data)) return true;
            Console.Error.WriteLine($"[Carbon] Tray: Shell_NotifyIcon({message}) failed.");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray update failed: {ex.Message}");
            return false;
        }
    }

    public static void Create(CarbonTrayBuilder builder, Action<CarbonTrayHandle>? onReady = null)
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
            FillMenu(_menu, builder.Items, ref id);
            _onEvent = builder.EventHandler;

            // Load the custom icon *before* building the struct — it reads _icon.
            if (builder.IconPath is { } iconPath) _icon = LoadIcon(iconPath);
            _tip = builder.Title;

            var data = new NOTIFYICONDATA
            {
                cbSize = sizeof(NOTIFYICONDATA),
                hWnd = _hwnd,
                uID = TrayIconId,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = CallbackMessage,
                hIcon = _icon != IntPtr.Zero ? _icon : LoadIconW(IntPtr.Zero, IDI_APPLICATION),
            };
            CopyTip(ref data, _tip);

            if (!Shell_NotifyIconW(NIM_ADD, ref data))
            {
                Console.Error.WriteLine("[Carbon] Tray: Shell_NotifyIcon(NIM_ADD) failed.");
                return;
            }

            _created = true;
            Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
            CarbonTray.NotifyReady(onReady);
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
            // Legacy notify-icon packing: the mouse message is in lParam, the icon id in wParam.
            var mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            ReportEvent(mouseMsg);
            if (mouseMsg is WM_RBUTTONUP or WM_LBUTTONUP)
                ShowMenu(hwnd);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Translate a notify-icon mouse message into a tray event (Task 2.8). Enter and Leave are absent:
    /// the shell only reports those as NIN_POPUPOPEN/NIN_POPUPCLOSE under NOTIFYICON_VERSION_4, which
    /// would also re-pack wParam/lParam for every other message here — not a trade worth making
    /// blind, since no one on this project can exercise a real Windows tray yet.
    /// </summary>
    private static void ReportEvent(int mouseMsg)
    {
        if (_onEvent is not { } handler) return;

        var (kind, button, state) = mouseMsg switch
        {
            WM_MOUSEMOVE => (CarbonTrayEventKind.Move, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
            WM_LBUTTONDOWN => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Down),
            WM_LBUTTONUP => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
            WM_RBUTTONDOWN => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Right, CarbonTrayButtonState.Down),
            WM_RBUTTONUP => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Right, CarbonTrayButtonState.Up),
            WM_MBUTTONDOWN => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Middle, CarbonTrayButtonState.Down),
            WM_MBUTTONUP => (CarbonTrayEventKind.Click, CarbonTrayMouseButton.Middle, CarbonTrayButtonState.Up),
            WM_LBUTTONDBLCLK => (CarbonTrayEventKind.DoubleClick, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
            WM_RBUTTONDBLCLK => (CarbonTrayEventKind.DoubleClick, CarbonTrayMouseButton.Right, CarbonTrayButtonState.Up),
            WM_MBUTTONDBLCLK => (CarbonTrayEventKind.DoubleClick, CarbonTrayMouseButton.Middle, CarbonTrayButtonState.Up),
            _ => (CarbonTrayEventKind.Move, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
        };
        if (kind == CarbonTrayEventKind.Move && mouseMsg != WM_MOUSEMOVE) return; // not a message we report

        try
        {
            GetCursorPos(out var cursor);
            handler(new CarbonTrayEvent(
                kind, button, state, new CarbonTrayPoint(cursor.X, cursor.Y), IconRect()));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray event failed: {ex.Message}");
        }
    }

    /// <summary>The icon's screen rect, which the shell can decline to give us.</summary>
    private static CarbonTrayRect IconRect()
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(NOTIFYICONIDENTIFIER),
            hWnd = _hwnd,
            uID = TrayIconId,
        };
        if (Shell_NotifyIconGetRect(ref id, out var rect) != 0) return default;
        return new CarbonTrayRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    /// Add tray items to an HMENU, recursing into submenus. A popup carries the submenu handle where a
    /// command id would go, so it never gets a handler; ids must stay unique across the whole tree,
    /// hence the counter by reference (same shape as WindowsMenu).
    /// </summary>
    private static void FillMenu(IntPtr menu, IReadOnlyList<TrayItem> items, ref int id)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }

            if (item.Children is { } children)
            {
                var child = CreatePopupMenu();
                FillMenu(child, children, ref id);
                AppendMenuW(menu, MF_POPUP, child, item.Label);
                continue;
            }

            AppendMenuW(menu, MF_STRING, id, item.Label);
            Handlers[id] = item.OnClick!;
            id++;
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);
    [DllImport("user32.dll")] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, IntPtr className, IntPtr windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, int type, int cx, int cy, int load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdiplus.dll")] private static extern int GdiplusStartup(out ulong token, ref GdiplusStartupInput input, IntPtr output);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] private static extern int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap);
    [DllImport("gdiplus.dll")] private static extern int GdipCreateHICONFromBitmap(IntPtr bitmap, out IntPtr hicon);
    [DllImport("gdiplus.dll")] private static extern int GdipDisposeImage(IntPtr image);

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, int idNewItem, string? newItem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int flags, IntPtr idNewItem, string? newItem);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr hMenu, int flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("shell32.dll")] private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATA data);
}
