---
title: Plugin scopes
description: Restrict sensitive plugin commands to approved resources.
---

Plugin configuration lives under `plugins.<namespace>` in `carbon.json`. Security-sensitive plugins
validate each request against that configuration after the capability check succeeds.

## File system

```json
{
  "plugins": {
    "fs": {
      "scopes": ["$APPDATA/**", "$DOCUMENT/**"]
    }
  }
}
```

Use the narrowest directories your app needs. File operations outside the configured scopes are denied.

## HTTP

```json
{
  "plugins": {
    "http": {
      "scope": ["https://api.example.com/v1/*"]
    }
  }
}
```

HTTP scope entries match scheme, host, and path prefix. A lookalike hostname such as
`api.example.com.evil` does not match `api.example.com`.

## Shell

```json
{
  "plugins": {
    "shell": {
      "allowedPrograms": ["git"],
      "allowedCwds": ["$APPDATA/repos"],
      "allowedEnv": ["PATH", "HOME"],
      "allowedUrlSchemes": ["https", "mailto"],
      "allowOpenPaths": false
    }
  }
}
```

Process execution is denied when `allowedPrograms` is empty. Avoid `allowedEnv: ["*"]`; pass only the
environment variables the child process requires. Use the Opener plugin instead of Shell for ordinary
open, reveal, and URL actions.
