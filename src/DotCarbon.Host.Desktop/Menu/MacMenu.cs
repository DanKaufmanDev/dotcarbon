using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// macOS application menu via NSApplication/NSMenu. Menu clicks are routed to C# handlers through a
/// runtime-created NSObject subclass whose action method is an unmanaged trampoline.
/// </summary>
internal static unsafe class MacMenu
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    private static readonly Dictionary<nint, Action> Handlers = new();
    private static CarbonMenuBuilder? _pending;
    private static nint _nextTag;
    private static IntPtr _target;
    private static bool _targetClassRegistered;

    public static void Create(CarbonMenuBuilder builder)
    {
        _pending = builder;
        var work = (IntPtr)(delegate* unmanaged<IntPtr, void>)&CreateOnMain;
        dispatch_async_f(MainQueue(), IntPtr.Zero, work);
    }

    [UnmanagedCallersOnly]
    private static void CreateOnMain(IntPtr context)
    {
        if (_pending is { } builder) CreateNow(builder);
    }

    private static void CreateNow(CarbonMenuBuilder builder)
    {
        try
        {
            var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
            var mainMenu = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
            _target = CreateActionTarget();

            foreach (var group in builder.Groups)
            {
                var rootItem = NewMenuItem(group.Label, IntPtr.Zero, string.Empty);
                var submenu = Send(Send(Cls("NSMenu"), Sel("alloc")), Sel("init"));
                SendPtr(submenu, Sel("setTitle:"), NSString(group.Label));

                foreach (var item in group.Items)
                {
                    var menuItem = item.IsSeparator
                        ? Send(Cls("NSMenuItem"), Sel("separatorItem"))
                        : NewActionItem(item);
                    SendPtr(submenu, Sel("addItem:"), menuItem);
                }

                SendPtr(rootItem, Sel("setSubmenu:"), submenu);
                SendPtr(mainMenu, Sel("addItem:"), rootItem);
            }

            SendPtr(app, Sel("setMainMenu:"), mainMenu);
            Console.WriteLine($"[Carbon] Native menu ready ({builder.Groups.Count} top-level menu(s)).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the macOS menu: {ex.Message}");
        }
    }

    private static IntPtr NewActionItem(MenuItem item)
    {
        var menuItem = NewMenuItem(item.Label!, Sel("carbonMenuClick:"), item.Shortcut);
        SendPtr(menuItem, Sel("setTarget:"), _target);
        var tag = _nextTag++;
        SendSetLong(menuItem, Sel("setTag:"), tag);
        Handlers[tag] = item.OnClick!;
        return menuItem;
    }

    private static IntPtr NewMenuItem(string label, IntPtr action, string shortcut) =>
        SendInitMenuItem(
            Send(Cls("NSMenuItem"), Sel("alloc")),
            Sel("initWithTitle:action:keyEquivalent:"),
            NSString(label),
            action,
            NSString(shortcut));

    private static IntPtr CreateActionTarget()
    {
        if (!_targetClassRegistered)
        {
            var cls = objc_allocateClassPair(Cls("NSObject"), "CarbonMenuTarget", 0);
            var imp = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMenuClick;
            class_addMethod(cls, Sel("carbonMenuClick:"), imp, "v@:@");
            objc_registerClassPair(cls);
            _targetClassRegistered = true;
        }
        return Send(Send(Cls("CarbonMenuTarget"), Sel("alloc")), Sel("init"));
    }

    [UnmanagedCallersOnly]
    private static void OnMenuClick(IntPtr self, IntPtr cmd, IntPtr sender)
    {
        var tag = SendGetLong(sender, Sel("tag"));
        if (!Handlers.TryGetValue(tag, out var handler)) return;
        try { handler(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu handler failed: {ex.Message}"); }
    }

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_allocateClassPair(IntPtr super, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);
    [DllImport(LibObjC)] private static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(LibObjC)] private static extern byte class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendStr(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendInitMenuItem(IntPtr receiver, IntPtr selector, IntPtr title, IntPtr action, IntPtr keyEquivalent);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendSetLong(IntPtr receiver, IntPtr selector, nint arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nint SendGetLong(IntPtr receiver, IntPtr selector);

    [DllImport(LibSystem)] private static extern void dispatch_async_f(IntPtr queue, IntPtr context, IntPtr work);

    private static IntPtr MainQueue() =>
        NativeLibrary.GetExport(NativeLibrary.Load(LibSystem), "_dispatch_main_q");

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr NSString(string value) =>
        SendStr(Cls("NSString"), Sel("stringWithUTF8String:"), value);
}
