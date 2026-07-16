---
title: Using NuGet packages
description: Use any compatible .NET library from a Carbon backend.
---

Carbon application logic is an ordinary .NET project. Add any compatible package directly:

```bash
carbon add nuget SharpHook
```

Specify a version or backend project when needed:

```bash
carbon add nuget Microsoft.Data.Sqlite --version 10.0.0
carbon add nuget Acme.Library --project src-carbon/MyApp.csproj
```

Use the package in C# as you would in any .NET application. Expose only the operations the frontend
needs through your own `[CarbonCommand]` methods.

```csharp
public partial class InputCommands : IPlugin
{
    public string Namespace => "input";

    [CarbonCommand("devices")]
    public DeviceInfo[] Devices() => DeviceScanner.GetConnectedDevices();
}
```

No Carbon-specific adapter is required for libraries used entirely in C#. A DotCarbon plugin is
useful when a package should provide reusable bridge commands, lifecycle integration, capabilities,
metadata, and a matching TypeScript API to several applications.

Before using NativeAOT, check the library's trimming and dynamic-code requirements. Reflection-heavy
packages may require serializer contexts, linker descriptors, or the regular self-contained build.
