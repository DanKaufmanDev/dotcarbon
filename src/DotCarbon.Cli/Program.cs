using System.CommandLine;
using DotCarbon.Cli.Commands;

var root = new RootCommand("⚡ Carbon — C# desktop app framework");

root.AddCommand(InitCommand.Build());
root.AddCommand(DevCommand.Build());
root.AddCommand(AddCommand.Build());
root.AddCommand(BundleCommand.Build());
root.AddCommand(BuildCommand.Build()); // Keep the original desktop build command as a bundle alias.
root.AddCommand(PlatformCommand.Build());
root.AddCommand(DoctorCommand.Build());
root.AddCommand(InfoCommand.Build());
root.AddCommand(TypesCommand.Build());
root.AddCommand(CapabilitiesCommand.Build());
root.AddCommand(PermissionCommand.Build());
root.AddCommand(PluginCommand.Build());
root.AddCommand(IconCommand.Build());
root.AddCommand(SignerCommand.Build());

return await root.InvokeAsync(args);
