---
title: Lifecycle
description: Observe and respond to application and window lifecycle events.
---

Register a lifecycle handler before running the app:

```csharp
CarbonApp.Create(config)
    .UseDesktop()
    .OnLifecycle(evt =>
    {
        Console.WriteLine($"{evt.Kind}: {evt.Window?.Label}");

        if (evt.Kind == CarbonLifecycleEventKind.ExitRequested && HasUnsavedWork())
            evt.Cancel = true;
    })
    .Run();
```

## Application events

- `Starting`
- `Ready`
- `ExitRequested`
- `Exiting`
- `Exited`

## Window events

- `WindowCreating` and `WindowCreated`
- `WindowCloseRequested` and `WindowClosed`
- `WindowFocused` and `WindowBlurred`
- `WindowMoved` and `WindowResized`
- `WindowMinimized`, `WindowMaximized`, and `WindowRestored`

`ExitRequested` and `WindowCloseRequested` can be cancelled. Plugins receive the same events through
`IPlugin.OnLifecycleAsync`, which is useful for flushing state or releasing native resources.

Desktop `Run()` starts the runtime, blocks in the platform message loop, and shuts down plugins in
reverse registration order. Mobile hosts call `Start()` and `Shutdown()` from their native lifecycle.
