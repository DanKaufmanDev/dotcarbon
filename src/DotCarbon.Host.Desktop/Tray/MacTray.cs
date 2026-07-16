using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// macOS system tray built with NSStatusItem and NSMenu through the Objective-C runtime.
/// </summary>
internal static unsafe class MacTray
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const double NSVariableStatusItemLength = -1.0;

    private static readonly Dictionary<nint, Action> Handlers = new();
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> MainQueueWork = new();
    private static CarbonTrayBuilder? _pending;
    private static Action<CarbonTrayHandle>? _onReady;
    private static nint _nextTag;
    private static IntPtr _statusItem;   // retained so it stays in the menu bar
    private static IntPtr _target;        // retained action target
    private static bool _targetClassRegistered;
    private static IntPtr _menu;          // retained; popped by hand on the takeover path
    private static Action<CarbonTrayEvent>? _onEvent;
    private static bool _menuOnLeftClick = true;

    // NSEventType (NSEvent.h)
    private const long NSEventTypeLeftMouseDown = 1;
    private const long NSEventTypeLeftMouseUp = 2;
    private const long NSEventTypeRightMouseDown = 3;
    private const long NSEventTypeRightMouseUp = 4;
    private const long NSEventTypeOtherMouseDown = 25;
    private const long NSEventTypeOtherMouseUp = 26;

    // NSEventMask — the button's action only fires for the event types we opt into here.
    private const nint NSEventMaskLeftMouseDown = 1 << 1;
    private const nint NSEventMaskLeftMouseUp = 1 << 2;
    private const nint NSEventMaskRightMouseDown = 1 << 3;
    private const nint NSEventMaskRightMouseUp = 1 << 4;
    private const nint NSEventMaskOtherMouseDown = 1 << 25;
    private const nint NSEventMaskOtherMouseUp = 1 << 26;

    // NSTrackingAreaOptions. InVisibleRect makes AppKit track the button's live bounds, which also
    // means the rect passed to initWithRect: is ignored — handy, since reading NSRect back through
    // objc_msgSend is ABI-sensitive.
    private const nint NSTrackingMouseEnteredAndExited = 0x01;
    private const nint NSTrackingMouseMoved = 0x02;
    private const nint NSTrackingActiveAlways = 0x80;
    private const nint NSTrackingInVisibleRect = 0x200;

    /// <summary>Schedules tray creation on the main queue after NSApplication starts.</summary>
    public static void Create(CarbonTrayBuilder builder, Action<CarbonTrayHandle>? onReady = null)
    {
        _pending = builder;
        _onReady = onReady;
        var work = (IntPtr)(delegate* unmanaged<IntPtr, void>)&CreateOnMain;
        dispatch_async_f(MainQueue(), IntPtr.Zero, work);
    }

    [UnmanagedCallersOnly]
    private static void CreateOnMain(IntPtr context)
    {
        if (_pending is { } builder) CreateNow(builder);
        CarbonTray.NotifyReady(_onReady);
        _onReady = null;
    }

    // --- runtime mutation (Task 2.3) ---------------------------------------------------------
    // AppKit is main-thread-only, so every setter is queued onto the main queue rather than run
    // wherever the caller happens to be.

    public static void SetTitle(string title) => Post(() =>
    {
        var button = _statusItem == IntPtr.Zero ? IntPtr.Zero : Send(_statusItem, Sel("button"));
        if (button != IntPtr.Zero) SendPtr(button, Sel("setTitle:"), NSString(title));
    });

    public static void SetTooltip(string tooltip) => Post(() =>
    {
        var button = _statusItem == IntPtr.Zero ? IntPtr.Zero : Send(_statusItem, Sel("button"));
        if (button != IntPtr.Zero) SendPtr(button, Sel("setToolTip:"), NSString(tooltip));
    });

    public static void SetIcon(string path, bool isTemplate) => Post(() => ApplyIcon(path, isTemplate));

    private static void ApplyIcon(string path, bool isTemplate)
    {
        var button = _statusItem == IntPtr.Zero ? IntPtr.Zero : Send(_statusItem, Sel("button"));
        if (button == IntPtr.Zero) return;

        var image = SendPtr(
            Send(Cls("NSImage"), Sel("alloc")), Sel("initWithContentsOfFile:"), NSString(path));
        if (image == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[Carbon] Tray: could not load the icon image: {path}");
            return;
        }

        // A template image is drawn as a mask, so it follows light/dark menu bars automatically.
        SendSetBool(image, Sel("setTemplate:"), isTemplate);
        SendPtr(button, Sel("setImage:"), image);
        Send(image, Sel("release"));
    }

    public static void SetVisible(bool visible) => Post(() =>
    {
        if (_statusItem != IntPtr.Zero) SendSetBool(_statusItem, Sel("setVisible:"), visible);
    });

    public static void Remove() => Post(() =>
    {
        if (_statusItem == IntPtr.Zero) return;
        SendPtr(Send(Cls("NSStatusBar"), Sel("systemStatusBar")), Sel("removeStatusItem:"), _statusItem);
        Send(_statusItem, Sel("release"));
        _statusItem = IntPtr.Zero;
    });

    private static void Post(Action work)
    {
        MainQueueWork.Enqueue(work);
        var trampoline = (IntPtr)(delegate* unmanaged<IntPtr, void>)&RunPendingWork;
        dispatch_async_f(MainQueue(), IntPtr.Zero, trampoline);
    }

    [UnmanagedCallersOnly]
    private static void RunPendingWork(IntPtr context)
    {
        while (MainQueueWork.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray update failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Replace the tray's menu (Task 2.11), leaving the icon and its click wiring alone. The takeover
    /// path holds the menu itself and pops it by hand, so swapping the field is enough there; the
    /// plain path has handed the menu to AppKit and has to hand it the new one. Old item tags belong
    /// to the discarded NSMenu and go with it.
    /// </summary>
    public static void RebuildMenu(CarbonTrayBuilder builder) => Post(() =>
    {
        if (_statusItem == IntPtr.Zero) return;

        Handlers.Clear();
        _nextTag = 0;

        var menu = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
        FillMenu(menu, builder.Items);
        Send(menu, Sel("retain"));

        var previous = _menu;
        _menu = menu;
        if (Send(_statusItem, Sel("menu")) != IntPtr.Zero) SendPtr(_statusItem, Sel("setMenu:"), menu);
        if (previous != IntPtr.Zero) Send(previous, Sel("release"));
    });

    private static void CreateNow(CarbonTrayBuilder builder)
    {
        try
        {
            var statusBar = Send(Cls("NSStatusBar"), Sel("systemStatusBar"));
            _statusItem = SendDouble(statusBar, Sel("statusItemWithLength:"), NSVariableStatusItemLength);
            if (_statusItem == IntPtr.Zero)
            {
                Console.Error.WriteLine("[Carbon] Tray: NSStatusBar returned no status item.");
                return;
            }
            Send(_statusItem, Sel("retain"));

            var button = Send(_statusItem, Sel("button"));
            if (button != IntPtr.Zero)
                SendPtr(button, Sel("setTitle:"), NSString(builder.Title));
            if (builder.IconPath is { } iconPath)
                ApplyIcon(iconPath, builder.IconIsTemplate);

            _target = CreateActionTarget();
            _onEvent = builder.EventHandler;
            _menuOnLeftClick = builder.MenuOnLeftClick;

            _menu = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
            FillMenu(_menu, builder.Items);
            Send(_menu, Sel("retain"));

            // Handing the menu to the status item lets AppKit own the button, which means it swallows
            // the clicks — so the only way to report pointer events (or to suppress the menu on left
            // click) is to keep the menu to ourselves and pop it by hand. Apps that want neither keep
            // the plain path, which stays exactly as it was.
            if (_onEvent is not null || !_menuOnLeftClick)
                TakeOverButton(button);
            else
                SendPtr(_statusItem, Sel("setMenu:"), _menu);

            Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the macOS tray: {ex.Message}");
        }
    }

    /// <summary>Add tray items to an NSMenu, recursing into submenus (Task 2.7).</summary>
    private static void FillMenu(IntPtr menu, IReadOnlyList<TrayItem> items)
    {
        foreach (var item in items)
        {
            IntPtr menuItem;
            if (item.IsSeparator)
            {
                menuItem = Send(Cls("NSMenuItem"), Sel("separatorItem"));
            }
            else if (item.Children is { } children)
            {
                // A submenu item has no action — AppKit opens the attached NSMenu.
                menuItem = Send(Send(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
                SendPtr(menuItem, Sel("setTitle:"), NSString(item.Label!));
                var child = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
                FillMenu(child, children);
                SendPtr(menuItem, Sel("setSubmenu:"), child);
            }
            else
            {
                menuItem = Send(Send(Cls("NSMenuItem"), Sel("alloc")), Sel("init"));
                SendPtr(menuItem, Sel("setTitle:"), NSString(item.Label!));
                SendPtr(menuItem, Sel("setTarget:"), _target);
                SendPtr(menuItem, Sel("setAction:"), Sel("carbonTrayClick:"));
                var tag = _nextTag++;
                SendSetLong(menuItem, Sel("setTag:"), tag);
                Handlers[tag] = item.OnClick!;
            }
            SendPtr(menu, Sel("addItem:"), menuItem);
        }
    }

    /// <summary>
    /// Route the status button's clicks to us instead of AppKit's menu handling, and start tracking
    /// the pointer for enter/move/leave (Task 2.8).
    /// </summary>
    private static void TakeOverButton(IntPtr button)
    {
        if (button == IntPtr.Zero) return;

        SendPtr(button, Sel("setTarget:"), _target);
        SendPtr(button, Sel("setAction:"), Sel("carbonTrayButton:"));
        // Buttons only send their action on mouse-up by default, which would lose every press and
        // every non-left button.
        SendSetLong(button, Sel("sendActionOn:"),
            NSEventMaskLeftMouseDown | NSEventMaskLeftMouseUp |
            NSEventMaskRightMouseDown | NSEventMaskRightMouseUp |
            NSEventMaskOtherMouseDown | NSEventMaskOtherMouseUp);

        if (_onEvent is null) return; // no one is listening; skip the tracking overhead

        var area = SendTrackingInit(
            Send(Cls("NSTrackingArea"), Sel("alloc")), Sel("initWithRect:options:owner:userInfo:"),
            default,
            NSTrackingMouseEnteredAndExited | NSTrackingMouseMoved | NSTrackingActiveAlways |
            NSTrackingInVisibleRect,
            _target, IntPtr.Zero);
        SendPtr(button, Sel("addTrackingArea:"), area);
    }

    private static IntPtr CreateActionTarget()
    {
        if (!_targetClassRegistered)
        {
            var cls = objc_allocateClassPair(Cls("NSObject"), "CarbonTrayTarget", 0);
            var imp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMenuClick;
            class_addMethod(cls, Sel("carbonTrayClick:"), imp, "v@:@");

            var buttonImp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnButtonAction;
            class_addMethod(cls, Sel("carbonTrayButton:"), buttonImp, "v@:@");

            // The tracking area's owner receives these; NSObject doesn't declare them, so add them.
            var enterImp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMouseEntered;
            class_addMethod(cls, Sel("mouseEntered:"), enterImp, "v@:@");
            var exitImp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMouseExited;
            class_addMethod(cls, Sel("mouseExited:"), exitImp, "v@:@");
            var moveImp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMouseMoved;
            class_addMethod(cls, Sel("mouseMoved:"), moveImp, "v@:@");

            objc_registerClassPair(cls);
            _targetClassRegistered = true;
        }
        return Send(Send(Cls("CarbonTrayTarget"), Sel("alloc")), Sel("init"));
    }

    // --- pointer events (Task 2.8) -----------------------------------------------------------

    [UnmanagedCallersOnly]
    private static void OnButtonAction(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        try
        {
            var nsEvent = Send(Send(Cls("NSApplication"), Sel("sharedApplication")), Sel("currentEvent"));
            if (nsEvent == IntPtr.Zero) return;

            var type = (long)SendGetLong(nsEvent, Sel("type"));
            var (button, state) = type switch
            {
                NSEventTypeLeftMouseDown => (CarbonTrayMouseButton.Left, CarbonTrayButtonState.Down),
                NSEventTypeLeftMouseUp => (CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
                NSEventTypeRightMouseDown => (CarbonTrayMouseButton.Right, CarbonTrayButtonState.Down),
                NSEventTypeRightMouseUp => (CarbonTrayMouseButton.Right, CarbonTrayButtonState.Up),
                NSEventTypeOtherMouseDown => (CarbonTrayMouseButton.Middle, CarbonTrayButtonState.Down),
                NSEventTypeOtherMouseUp => (CarbonTrayMouseButton.Middle, CarbonTrayButtonState.Up),
                _ => (CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up),
            };

            // A double click arrives as a second up with clickCount 2; report it as its own kind the
            // way Tauri does rather than as another plain Click.
            var isDouble = state == CarbonTrayButtonState.Up &&
                (long)SendGetLong(nsEvent, Sel("clickCount")) == 2;
            Emit(isDouble ? CarbonTrayEventKind.DoubleClick : CarbonTrayEventKind.Click, button, state);

            // The menu opens on press, matching every other menu-bar item.
            var opensMenu = state == CarbonTrayButtonState.Down &&
                (button == CarbonTrayMouseButton.Right ||
                    (button == CarbonTrayMouseButton.Left && _menuOnLeftClick));
            if (opensMenu) PopUpMenu(sender);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray event failed: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnMouseEntered(IntPtr self, IntPtr cmd, IntPtr nsEvent) =>
        EmitPointer(CarbonTrayEventKind.Enter);

    [UnmanagedCallersOnly]
    private static void OnMouseExited(IntPtr self, IntPtr cmd, IntPtr nsEvent) =>
        EmitPointer(CarbonTrayEventKind.Leave);

    [UnmanagedCallersOnly]
    private static void OnMouseMoved(IntPtr self, IntPtr cmd, IntPtr nsEvent) =>
        EmitPointer(CarbonTrayEventKind.Move);

    private static void EmitPointer(CarbonTrayEventKind kind)
    {
        try { Emit(kind, CarbonTrayMouseButton.Left, CarbonTrayButtonState.Up); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray event failed: {ex.Message}"); }
    }

    private static void Emit(CarbonTrayEventKind kind, CarbonTrayMouseButton button, CarbonTrayButtonState state)
    {
        if (_onEvent is not { } handler) return;

        var cursor = SendGetPoint(Cls("NSEvent"), Sel("mouseLocation"));
        handler(new CarbonTrayEvent(
            kind, button, state,
            new CarbonTrayPoint(cursor.X, cursor.Y),
            IconRect()));
    }

    /// <summary>The icon's screen rect: the status item's window frame.</summary>
    private static CarbonTrayRect IconRect()
    {
        var button = _statusItem == IntPtr.Zero ? IntPtr.Zero : Send(_statusItem, Sel("button"));
        var window = button == IntPtr.Zero ? IntPtr.Zero : Send(button, Sel("window"));
        if (window == IntPtr.Zero) return default;

        var frame = GetRect(window, Sel("frame"));
        return new CarbonTrayRect(frame.X, frame.Y, frame.Width, frame.Height);
    }

    private static void PopUpMenu(IntPtr button)
    {
        if (_menu == IntPtr.Zero || button == IntPtr.Zero) return;
        // Positioning in the button's own coordinates drops the menu directly under the icon.
        SendPopUp(_menu, Sel("popUpMenuPositioningItem:atLocation:inView:"),
            IntPtr.Zero, default, button);
    }

    [UnmanagedCallersOnly]
    private static void OnMenuClick(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        var tag = SendGetLong(sender, Sel("tag"));
        if (!Handlers.TryGetValue(tag, out var handler)) return;
        try { handler(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray handler failed: {ex.Message}"); }
    }

    // Objective-C runtime and libdispatch interop

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr super, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);
    [DllImport(LibObjC)] private static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(LibObjC)] private static extern byte class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendStr(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendSetLong(IntPtr receiver, IntPtr selector, nint arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendSetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nint SendGetLong(IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
    }

    // NSPoint is two doubles, which both ABIs return in registers, so plain objc_msgSend is correct.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern CGPoint SendGetPoint(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPopUp(IntPtr receiver, IntPtr selector, IntPtr item, CGPoint location, IntPtr view);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendTrackingInit(IntPtr receiver, IntPtr selector, CGRect rect, nint options, IntPtr owner, IntPtr userInfo);

    // NSRect is 32 bytes: arm64 returns it in v0-v3 (and has no objc_msgSend_stret at all), while
    // x86_64 classes it as MEMORY and returns it through a hidden pointer — i.e. a different entry
    // point. Calling the wrong one yields garbage rather than a crash, so pick by architecture.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern CGRect SendGetRect(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend_stret")] private static extern void SendGetRectStret(out CGRect result, IntPtr receiver, IntPtr selector);

    private static CGRect GetRect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return SendGetRect(receiver, selector);
        SendGetRectStret(out var rect, receiver, selector);
        return rect;
    }

    [DllImport(LibSystem)] private static extern void dispatch_async_f(IntPtr queue, IntPtr context, IntPtr work);

    private static IntPtr MainQueue() =>
        NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_dispatch_main_q");

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr NSString(string value) =>
        SendStr(Cls("NSString"), Sel("stringWithUTF8String:"), value);
}
