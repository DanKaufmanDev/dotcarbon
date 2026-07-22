using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Dialog;

/// <summary>
/// Exposes the platform host's native dialogs as commands. The host owns modal UI (see
/// <see cref="ICarbonDialogs"/>), so this plugin holds no platform reference and runs on desktop and
/// mobile alike. Mobile hosts implement the alerts natively; their file choosers report as unsupported.
/// </summary>
[CarbonPlugin("Dialogs", description: "Open native file, folder, message, and confirmation dialogs.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("dialog:default", "Allow all dialog commands.", Commands = new[] { "dialog:*" })]
public partial class DialogPlugin : IPlugin
{
    private readonly AppHandle? _app;
    private readonly ICarbonDialogs? _dialogs;

    public DialogPlugin(AppHandle app) => _app = app;

    // Injection seam for tests.
    internal DialogPlugin(ICarbonDialogs dialogs) => _dialogs = dialogs;

    public string Namespace => "dialog";

    private ICarbonDialogs Dialogs =>
        _dialogs ?? _app?.PlatformDialogs
        ?? throw new NotSupportedException("The current platform host does not provide native dialogs.");

    [CarbonCommand("open_file")]
    public Task<string[]?> OpenFile(OpenFileArgs args) =>
        Dialogs.OpenFileAsync(args.Title, args.DefaultPath, args.Multiple, args.Filters);

    [CarbonCommand("save_file")]
    public Task<string?> SaveFile(SaveFileArgs args) =>
        Dialogs.SaveFileAsync(args.Title, args.DefaultPath ?? args.DefaultName);

    [CarbonCommand("open_folder")]
    public Task<string?> OpenFolder(OpenFolderArgs args) =>
        Dialogs.OpenFolderAsync(args.Title, args.DefaultPath);

    [CarbonCommand("message")]
    public Task Message(MessageArgs args) => Dialogs.MessageAsync(args.Title, args.Message);

    [CarbonCommand("confirm")]
    public Task<bool> Confirm(ConfirmArgs args) => Dialogs.ConfirmAsync(args.Title, args.Message);
}
