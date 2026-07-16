---
title: Frontend API
description: Core TypeScript exports from @dotcarbon/api.
---

## `invoke`

```ts
function invoke<K extends string>(
  command: K,
  args?: CommandArgs<K>,
): Promise<CommandResult<K>>
```

Sends a command to the native backend. Generated application declarations and installed plugin
packages augment `CarbonCommands` for name, payload, and result inference. A backend failure rejects
the promise with an `Error`.

## `emit`

```ts
function emit<K extends string>(
  event: K,
  payload: EventPayload<K>,
  options?: { target?: EventTarget },
): Promise<void>
```

Targets are `all`, `app`, a window label string, `{ kind: 'all' | 'app' }`, or
`{ kind: 'window', label: string }`.

## `listen`

```ts
function listen<K extends string>(
  event: K,
  handler: (event: CarbonEvent<EventPayload<K>>) => void,
): Promise<() => void>
```

The returned function removes that listener. `CarbonEvent` contains `id`, `event`, `payload`, and the
source window label when available.

## `once`

Registers a listener that removes itself before its first callback. It also returns an unlisten function
in case the caller cancels before the event arrives.

## `unlisten`

```ts
function unlisten(event: string, listenerId?: number): void
```

Without an ID, removes all local listeners for the event. Application code normally uses the function
returned by `listen` or `once`.

## `isCarbonApp`

Returns `true` when the native bridge is available. Use it when a frontend also runs in a regular
browser and needs a browser-safe fallback.

## Metadata types

The package exports `CarbonPluginMetadata`, `CarbonCommandMetadata`, `CarbonPermissionMetadata`, and
`CarbonEventMetadata`. Generated `carbon.d.ts` files use these types for plugin metadata declarations.
