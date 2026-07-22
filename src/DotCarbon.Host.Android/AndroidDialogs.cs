using Android.App;
using Android.Content;
using Android.OS;
using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// Android native dialogs: message/confirm via <see cref="AlertDialog"/> on the main looper. File
/// choosers need Storage Access Framework activity-result plumbing and are not supported yet.
/// </summary>
internal sealed class AndroidDialogs : ICarbonDialogs
{
    private const string NoFileDialogs =
        "File dialogs are not supported on Android. Use the app's sandbox paths (path:app_data_dir) " +
        "or a Storage Access Framework picker.";

    private readonly Func<Context> _context;

    public AndroidDialogs(Func<Context> context) => _context = context;

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
        // Dialogs must be built and shown on the UI thread.
        new Handler(Looper.MainLooper!).Post(() =>
        {
            try
            {
                var builder = new AlertDialog.Builder(_context())
                    .SetTitle(title)!
                    .SetMessage(message)!
                    .SetCancelable(false)!
                    .SetPositiveButton("OK", (_, _) => completion.TrySetResult(true))!;
                if (withCancel)
                    builder = builder.SetNegativeButton("Cancel", (_, _) => completion.TrySetResult(false))!;
                builder.Show();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }
}
