---
title: Notification
description: Send native desktop notifications.
---

```bash
carbon add plugin Notification
```

```ts
import { notification } from '@dotcarbon/plugin-notification'

await notification.send({
  title: 'Export complete',
  body: 'The profile package is ready.',
  subtitle: 'Carbon Profiles',
})
```

Grant `notification:*` to the calling window. Operating systems may require the packaged application
identity or user approval before displaying notifications reliably.
