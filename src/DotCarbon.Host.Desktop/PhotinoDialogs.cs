using DotCarbon.Core.Host;
using Photino.NET;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Desktop native dialogs, backed by the main Photino window. This is the same behaviour the Dialog
/// plugin used to implement directly — moving it here keeps Photino out of the plugin so the plugin
/// can also run on mobile.
/// </summary>
internal sealed class PhotinoDialogs : ICarbonDialogs
{
    private readonly PhotinoWindow _window;

    public PhotinoDialogs(PhotinoWindow window) => _window = window;

    public Task MessageAsync(string title, string message)
    {
        _window.ShowMessage(title, message);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        Task.FromResult(_window.ShowMessage(title, message) == 0);

    public Task<string[]?> OpenFileAsync(string title, string? defaultPath, bool multiple, string[]? filters) =>
        Task.FromResult<string[]?>(_window.ShowOpenFile(
            title: title, defaultPath: defaultPath, multiSelect: multiple, filters: ParseFilters(filters)));

    public Task<string?> SaveFileAsync(string title, string? defaultPath) =>
        Task.FromResult<string?>(_window.ShowSaveFile(title: title, defaultPath: defaultPath));

    public Task<string?> OpenFolderAsync(string title, string? defaultPath) =>
        Task.FromResult(_window
            .ShowOpenFile(title: title, defaultPath: defaultPath, multiSelect: false, filters: null)
            ?.FirstOrDefault());

    private static (string Name, string[] Extensions)[]? ParseFilters(string[]? filters)
    {
        if (filters is null || filters.Length == 0) return null;

        return filters
            .Select(filter =>
            {
                var extension = filter.TrimStart('*', '.');
                return (Name: $"{extension} files", Extensions: new[] { extension });
            })
            .ToArray();
    }
}
