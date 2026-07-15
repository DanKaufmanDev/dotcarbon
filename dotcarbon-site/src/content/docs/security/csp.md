---
title: Content Security Policy
description: Apply a CSP to embedded Carbon frontend assets.
---

Set the policy under `security.csp`:

```json
{
  "security": {
    "contentSecurityPolicy": "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self' https://api.example.com"
  }
}
```

Carbon injects the configured policy while serving production HTML through `carbon://localhost`.
Develop the frontend with external scripts and styles so the production policy does not require
`'unsafe-inline'`.

`connect-src` controls browser-originated network requests. Requests made through the HTTP plugin are
also checked against `plugins.http.scope`, providing a separate backend boundary.

Test packaged output, not only the Vite server: development tooling often needs websocket connections,
inline styles, or evaluation that should not be present in a release policy.
