---
title: Capabilities
description: Assign bridge permissions to labeled windows.
---

Capability files live in `src-carbon/capabilities/*.json` or `capabilities/*.json`. The filename is the
identifier when the JSON omits one.

```json
{
  "identifier": "main",
  "description": "Permissions used by the main application window.",
  "windows": ["main"],
  "commands": [
    "app:*",
    "store:*",
    "dialog:open_file",
    "core:event_emit"
  ]
}
```

Attach capability identifiers to a window:

```json
{
  "window": {
    "label": "main",
    "capabilities": ["main"]
  }
}
```

Patterns may be exact (`dialog:open_file`), namespace wildcards (`store:*`), or the global wildcard
`*`. Prefer exact commands or narrowly grouped namespaces. `permissions` is accepted as an alias for
`commands` in capability files.

## CLI

```bash
carbon capabilities list
carbon capabilities add store:default --capability main --window main
carbon capabilities sync
carbon capabilities check
```

`carbon dev` and `carbon types` sync local `[CarbonCommand]` methods into the main capability by
default. Plugin permissions remain explicit and are added by `carbon add plugin` when the catalog
provides a recommended permission.

Capabilities declared inline under `security.capabilities` are merged with external files before the
effective production manifest is embedded.
