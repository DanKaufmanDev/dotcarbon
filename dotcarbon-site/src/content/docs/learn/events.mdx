---
title: Events
description: Emit and listen to typed events across C# and webviews.
---

Define event names with their payload types:

```csharp
public static class AppEvents
{
    public static readonly CarbonEventName<int> CountChanged = new("count:changed");
}
```

Emit to every listener, only the C# application, or one labeled window:

```csharp
await app.EmitAsync(AppEvents.CountChanged, 4);
await app.EmitAsync(
    AppEvents.CountChanged,
    5,
    CarbonEventTarget.Window("settings"));
```

Listen repeatedly or once. Dispose the returned subscription to unlisten.

```csharp
using var subscription = app.Events.Listen(
    AppEvents.CountChanged,
    evt => Console.WriteLine(evt.Payload));

app.Events.Once(AppEvents.CountChanged, evt => Save(evt.Payload));
```

For trimmed or NativeAOT applications, use overloads that accept `JsonTypeInfo<T>` from a generated
`JsonSerializerContext`.

## Frontend events

Augment the event map when defining application events:

```ts
declare module '@dotcarbon/api' {
  interface CarbonEvents {
    'count:changed': number
  }
}
```

```ts
import { emit, listen, once } from '@dotcarbon/api'

const stop = await listen('count:changed', event => {
  console.log(event.payload, event.source)
})

await emit('count:changed', 6)
await emit('count:changed', 7, {
  target: { kind: 'window', label: 'settings' },
})

await once('count:changed', event => console.log(event.payload))
stop()
```

Frontend emission requires the `core:event_emit` capability.
