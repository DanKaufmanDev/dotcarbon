---
title: Clipboard
description: Read, write, and clear the desktop text clipboard.
---

```bash
carbon add plugin Clipboard
```

```ts
import { clipboard } from '@dotcarbon/plugin-clipboard'

await clipboard.writeText('Carbon')
const value = await clipboard.readText()
await clipboard.clear()
```

Grant `clipboard:*` or the `clipboard:default` permission to each window that needs access. The current
plugin handles text clipboard content on desktop hosts.
