---
title: Icons
description: Generate desktop and mobile icon assets from one source image.
---

Place a square 1024 x 1024 PNG at:

```text
src-carbon/icons/icon.png
```

Generate the complete platform set:

```bash
carbon icon
```

Carbon produces Windows ICO sizes, a macOS ICNS, Linux PNG and hicolor assets, Android density
resources, and the iOS asset catalog inputs used by generated platform projects.

Use a full-resolution source with transparent edges where appropriate. Keep critical artwork away
from the outer edge because macOS, Android, and iOS apply different masks. Avoid pre-rounding the source;
the platform should own the final shape.

Production builds refresh generated assets when the source icon is newer. Commit the source icon;
generated platform assets may be regenerated during platform sync and CI.
