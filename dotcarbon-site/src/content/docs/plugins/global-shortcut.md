---
title: Global Shortcut
description: Register and observe system-wide keyboard shortcuts.
---

```bash
carbon add plugin GlobalShortcut
```

```ts
import { globalShortcut } from '@dotcarbon/plugin-global-shortcut'

await globalShortcut.register({
  id: 'open-command-palette',
  accelerator: 'CmdOrCtrl+Shift+P',
})

const stop = await globalShortcut.onPressed(event => {
  if (event.id === 'open-command-palette') openPalette()
})

await globalShortcut.unregister('open-command-palette')
```

Use stable IDs so registrations can be replaced and removed. `suppress` requests that Carbon consume
the shortcut instead of passing it to the focused application; platform behavior can differ.
