---
title: Commands
description: Expose typed C# operations to the frontend.
---

A command is a C# method addressed as `namespace:name`. Commands belong to a partial `IPlugin` class
so the source generator can emit its registration code.

```csharp
public record CreateNoteArgs(string Title, string Body);
public record Note(string Id, string Title, string Body);

public partial class AppCommands : IPlugin
{
    public string Namespace => "app";

    [CarbonCommand("create_note")]
    public Note CreateNote(CreateNoteArgs args) =>
        new(Guid.NewGuid().ToString("N"), args.Title, args.Body);
}
```

Register the plugin with the application:

```csharp
CarbonApp.Create(config)
    .UseDesktop()
    .WithPlugin<AppCommands>()
    .Run();
```

Call it from TypeScript:

```ts
import { invoke } from '@dotcarbon/api'

const note = await invoke('app:create_note', {
  title: 'Release',
  body: 'Prepare signed artifacts',
})
```

## Signatures

Commands accept zero or one argument. Use a record or class when several values are needed. Methods
may return a value, `void`, `Task`, or `Task<T>`. JSON property names are camel-cased across the bridge.

Use DTOs made from primitives, enums, arrays, lists, dictionaries, records, and serializable classes.
Avoid passing native handles or service instances through a command result.

## Errors

Exceptions become rejected frontend promises. Return an application result type when callers need to
distinguish expected outcomes; reserve exceptions for failures that should abort the operation.

```ts
try {
  await invoke('app:create_note', input)
} catch (error) {
  console.error(String(error))
}
```

## Calling window

During a command, `AppHandle.CurrentWindow` identifies the webview that invoked it. This lets one
handler respond to the correct window without accepting a user-controlled label in the payload.
