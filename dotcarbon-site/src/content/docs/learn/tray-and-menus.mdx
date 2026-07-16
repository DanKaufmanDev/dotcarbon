---
title: Tray & menus
description: Add native desktop tray and application menus from C#.
---

Tray and menu builders are provided by `DotCarbon.Host.Desktop`.

```csharp
CarbonApp.Create(config)
    .UseDesktop()
    .UseTray(tray => tray
        .SetTitle("C")
        .AddEventItem("Open", "tray:open")
        .AddSeparator()
        .AddItem("Quit", () => Environment.Exit(0)))
    .UseMenu(menu => menu
        .AddMenu("File", file => file
            .AddEventItem("New", "menu:new", "CmdOrCtrl+N")
            .AddSeparator()
            .AddItem("Quit", () => Environment.Exit(0))))
    .Run();
```

`AddEventItem` emits a Carbon event that frontend or C# listeners can consume. `AddItem` executes a
C# callback directly.

The tray has native implementations for macOS, Windows, and GTK-based Linux desktops. Some Linux
desktop environments hide legacy status icons without an extension. Application menu support is
currently native on macOS; Windows and Linux report the unsupported path without failing startup.
