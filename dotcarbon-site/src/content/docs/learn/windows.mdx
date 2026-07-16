---
title: Windows & webviews
description: Create and control labeled desktop windows.
---

Every desktop window has a unique application label and one webview. The main window uses the label
configured in `window.label`, which defaults to `main`.

## Declare startup windows

```json
{
  "window": { "label": "main", "title": "Notes" },
  "windows": [
    {
      "label": "settings",
      "title": "Settings",
      "url": "settings.html",
      "width": 720,
      "height": 520,
      "capabilities": ["settings"]
    }
  ]
}
```

## Create a window in C#

```csharp
.Setup(app =>
{
    app.CreateWindow("preview", options =>
    {
        options.Title = "Preview";
        options.Url = "preview.html";
        options.Width = 900;
        options.Height = 700;
        options.Capabilities.Add("preview");
    });
})
```

Relative URLs resolve against the Vite server in development and embedded assets in production.

## Control windows from TypeScript

```bash
carbon add plugin Window
```

```ts
import { WebviewWindow } from '@dotcarbon/plugin-window'

const settings = await WebviewWindow.getByLabel('settings')
await settings?.setTitle('Application settings')
await settings?.center()

const windows = await WebviewWindow.getAll()
```

The Window plugin supports creation, lookup, state, title, size, position, centering, minimize,
maximize, fullscreen, always-on-top, resizable state, and close.

Mobile hosts currently expose one full-screen webview rather than desktop-style window management.
