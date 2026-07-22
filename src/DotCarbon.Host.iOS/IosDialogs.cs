using CoreFoundation;
using DotCarbon.Core.Host;
using UIKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// iOS native dialogs: message/confirm via <see cref="UIAlertController"/> presented on the root view
/// controller. File choosers need UIDocumentPicker delegate plumbing and are not supported yet.
/// </summary>
internal sealed class IosDialogs : ICarbonDialogs
{
    private const string NoFileDialogs =
        "File dialogs are not supported on iOS. Use the app's sandbox paths (path:app_data_dir) " +
        "or a UIDocumentPicker-based plugin.";

    private readonly Func<UIViewController?> _rootController;

    public IosDialogs(Func<UIViewController?> rootController) => _rootController = rootController;

    public Task MessageAsync(string title, string message) => Show(title, message, withCancel: false);

    public Task<bool> ConfirmAsync(string title, string message) => Show(title, message, withCancel: true);

    public Task<string[]?> OpenFileAsync(string title, string? defaultPath, bool multiple, string[]? filters) =>
        throw new NotSupportedException(NoFileDialogs);

    public Task<string?> SaveFileAsync(string title, string? defaultPath) =>
        throw new NotSupportedException(NoFileDialogs);

    public Task<string?> OpenFolderAsync(string title, string? defaultPath) =>
        throw new NotSupportedException(NoFileDialogs);

    private Task<bool> Show(string title, string message, bool withCancel)
    {
        var completion = new TaskCompletionSource<bool>();
        // UIKit presentation must happen on the main queue.
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var controller = _rootController();
            if (controller is null)
            {
                completion.TrySetResult(false);
                return;
            }

            var alert = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
            alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, _ => completion.TrySetResult(true)));
            if (withCancel)
                alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => completion.TrySetResult(false)));
            controller.PresentViewController(alert, animated: true, completionHandler: null);
        });
        return completion.Task;
    }
}
