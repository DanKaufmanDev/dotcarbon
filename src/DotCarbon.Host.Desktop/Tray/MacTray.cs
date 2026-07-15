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

            _target = CreateActionTarget();

            var menu = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
            foreach (var item in builder.Items)
            {
                IntPtr menuItem;
                if (item.IsSeparator)
                {
                    menuItem = Send(Cls("NSMenuItem"), Sel("separatorItem"));
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
            SendPtr(_statusItem, Sel("setMenu:"), menu);
            Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the macOS tray: {ex.Message}");
        }
    }

    private static IntPtr CreateActionTarget()
    {
        if (!_targetClassRegistered)
        {
            var cls = objc_allocateClassPair(Cls("NSObject"), "CarbonTrayTarget", 0);
            var imp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMenuClick;
            class_addMethod(cls, Sel("carbonTrayClick:"), imp, "v@:@");
            objc_registerClassPair(cls);
            _targetClassRegistered = true;
        }
        return Send(Send(Cls("CarbonTrayTarget"), Sel("alloc")), Sel("init"));
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

    [DllImport(LibSystem)] private static extern void dispatch_async_f(IntPtr queue, IntPtr context, IntPtr work);

    private static IntPtr MainQueue() =>
        NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_dispatch_main_q");

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr NSString(string value) =>
        SendStr(Cls("NSString"), Sel("stringWithUTF8String:"), value);
}
