---
title: Store
description: Persist JSON-compatible application settings.
---

```bash
carbon add plugin Store
```

```ts
import { store } from '@dotcarbon/plugin-store'

await store.set('theme', 'dark')
const theme = await store.get<string>('theme')

await store.set('profiles', profiles, { store: 'remapper' })
const snapshot = await store.entries({ store: 'remapper' })
const keys = await store.keys({ store: 'remapper' })

await store.delete('theme')
await store.clear({ store: 'remapper' })
```

Values are JSON-compatible and persisted under the application's data directory. Named stores keep
unrelated settings in separate files. Store is intended for preferences and small state, not large
documents, secrets, or relational data.
