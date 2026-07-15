---
title: Single Instance
description: Prevent duplicate desktop processes and identify the primary instance.
---

```bash
carbon add plugin SingleInstance
```

```ts
import { singleInstance } from '@dotcarbon/plugin-single-instance'

const status = await singleInstance.status()
if (!status.isPrimary) console.log('Arguments were forwarded to the primary app')
```

The plugin derives a stable mutex name from application identity. Secondary launches forward their
arguments or URL inputs to the primary process and then exit. Register it early when deep links or
file associations must be routed to an existing window.
