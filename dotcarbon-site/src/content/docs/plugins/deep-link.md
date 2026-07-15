---
title: Deep Link
description: Receive URLs opened through application protocol handlers.
---

```bash
carbon add plugin DeepLink
```

Register schemes in `bundle.protocols`:

```json
{
  "bundle": {
    "protocols": [{ "name": "Notes URL", "schemes": ["carbon-notes"] }]
  }
}
```

```ts
import { deepLink } from '@dotcarbon/plugin-deep-link'

const pending = await deepLink.getPending()
const stop = await deepLink.onOpen(url => routeUrl(url))
```

The plugin queues URLs received before the frontend subscribes and emits `deep-link:opened` for later
launches. Pair it with Single Instance so a second process forwards URLs to the running app.
