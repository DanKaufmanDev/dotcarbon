using System.CommandLine;
using DotCarbon.Cli.Commands;

var root = new RootCommand("⚡ Carbon — C# desktop app framework");

root.AddCommand(DevCommand.Build());
root.AddCommand(AddCommand.Build());
root.AddCommand(BundleCommand.Build());
root.AddCommand(BuildCommand.Build()); // alias for `carbon bundle desktop` — same engine
root.AddCommand(PlatformCommand.Build());
root.AddCommand(DoctorCommand.Build());
root.AddCommand(TypesCommand.Build());
root.AddCommand(IconCommand.Build());
root.AddCommand(SignerCommand.Build());

return await root.InvokeAsync(args);
