---
title: Updater
description: Check, download, verify, and install signed desktop updates.
---

```bash
carbon add plugin Updater
```

Configure endpoints and the public signing key under `bundle.updater`, then grant `updater:*`.

```ts
import { updater } from '@dotcarbon/plugin-updater'

const update = await updater.check()
if (update.available) {
  const download = await updater.download()
  console.log(download.signatureVerified, download.path)
  await updater.installAndRestart({ path: download.path })
}
```

Downloads are checked against the manifest SHA-256 and ECDSA signature before installation. The
private key belongs only in CI; applications embed the public key used for verification.

See [Updater artifacts](/distribute/updater/) for release-side signing and manifest generation.
