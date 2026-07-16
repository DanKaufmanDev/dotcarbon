---
title: Type generation
description: Generate TypeScript declarations and capability entries from C# commands.
---

`carbon types` scans application command classes and writes `ui/src/carbon.d.ts`:

```bash
carbon types
```

The declaration augments `CarbonCommands` in `@dotcarbon/api`, making command names, payloads, and
results available to TypeScript inference.

```ts
const value = await invoke('app:sum', { a: 2, b: 3 }) // number
```

The generated file is replaced on each run. Keep it in the frontend TypeScript program, but do not
edit it manually.

## Automatic development sync

`carbon dev` runs type generation at startup and watches C# sources. It also adds newly discovered
application commands to `src-carbon/capabilities/main.json`, keeping the ordinary edit-run loop
usable with security enabled.

Pass `--no-capabilities` when capability files are curated manually:

```bash
carbon types --no-capabilities
carbon dev --no-capabilities
```

Plugin npm packages augment `CarbonCommands` themselves, so installing a first-party frontend
wrapper immediately adds its command types without regenerating application declarations.
