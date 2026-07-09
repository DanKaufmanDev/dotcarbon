using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using Photino.NET;

namespace DotCarbon.Plugins.Dialog;

public partial class DialogPlugin : IPlugin
{
    public string Namespace => "dialog";

    private readonly PhotinoWindow _window;

    public DialogPlugin(PhotinoWindow window)
    {
        _window = window;
    }

    [CarbonCommand("open_file")]
    public Task<string[]?> OpenFile(OpenFileArgs args)
    {
        var results = _window.ShowOpenFile(
            title: args.Title,
            defaultPath: args.DefaultPath,
            multiSelect: args.Multiple,
            filters: ParseFilters(args.Filters)
        );

        return Task.FromResult<string[]?>(results);
    }

    [CarbonCommand("save_file")]
    public Task<string?> SaveFile(SaveFileArgs args)
    {
        var result = _window.ShowSaveFile(
            title: args.Title,
            defaultPath: args.DefaultPath ?? args.DefaultName
        );

        return Task.FromResult<string?>(result);
    }

    [CarbonCommand("open_folder")]
    public Task<string?> OpenFolder(OpenFolderArgs args)
    {
        var results = _window.ShowOpenFile(
            title: args.Title,
            defaultPath: args.DefaultPath,
            multiSelect: false,
            filters: null
        );

        return Task.FromResult(results?.FirstOrDefault());
    }

    [CarbonCommand("message")]
    public Task Message(MessageArgs args)
    {
        _window.ShowMessage(args.Title, args.Message);
        return Task.CompletedTask;
    }

    [CarbonCommand("confirm")]
    public Task<bool> Confirm(ConfirmArgs args)
    {
        var result = _window.ShowMessage(
            args.Title,
            args.Message
        );

        return Task.FromResult(result == 0);
    }

    private static (string Name, string[] Extensions)[]? ParseFilters(string[]? filters)
    {
        if (filters is null || filters.Length == 0)
            return null;

        return filters
            .Select(f => {
                var ext = f.TrimStart('*', '.');
                return (Name: $"{ext} files", Extensions: new[] { ext });
            })
            .ToArray();
    }
}