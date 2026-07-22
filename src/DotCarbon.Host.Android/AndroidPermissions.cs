using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.Content.PM;
using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// Android runtime permissions. Prompting is asynchronous and answered on the Activity, so each request
/// parks a completion keyed by request code that <see cref="CarbonActivity.OnRequestPermissionsResult"/>
/// resolves.
/// </summary>
internal sealed class AndroidPermissions : ICarbonPermissions
{
    /// <summary>Carbon permission id → the Android permissions that back it.</summary>
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["camera"] = [global::Android.Manifest.Permission.Camera],
        ["microphone"] = [global::Android.Manifest.Permission.RecordAudio],
        ["location"] = [global::Android.Manifest.Permission.AccessFineLocation, global::Android.Manifest.Permission.AccessCoarseLocation],
        ["notifications"] = ["android.permission.POST_NOTIFICATIONS"],
        ["contacts"] = [global::Android.Manifest.Permission.ReadContacts],
        ["photoLibrary"] = ["android.permission.READ_MEDIA_IMAGES"],
    };

    private static readonly ConcurrentDictionary<int, TaskCompletionSource<string>> Pending = new();
    private static int _nextRequestCode = 9100;

    private readonly Func<Context> _context;

    public AndroidPermissions(Func<Context> context) => _context = context;

    public Task<string> StatusAsync(string permission)
    {
        if (!Map.TryGetValue(permission, out var required))
            return Task.FromResult(CarbonPermissionState.Unsupported);

        // POST_NOTIFICATIONS only exists on API 33+; below that posting is allowed outright.
        if (string.Equals(permission, "notifications", StringComparison.OrdinalIgnoreCase) &&
            !OperatingSystem.IsAndroidVersionAtLeast(33))
            return Task.FromResult(CarbonPermissionState.Granted);

        var context = _context();
        // Any of the backing permissions being granted counts (fine OR coarse location, say).
        var granted = required.Any(name => context.CheckSelfPermission(name) == Permission.Granted);
        return Task.FromResult(granted ? CarbonPermissionState.Granted : CarbonPermissionState.Prompt);
    }

    public async Task<string> RequestAsync(string permission)
    {
        var status = await StatusAsync(permission);
        if (status is CarbonPermissionState.Granted or CarbonPermissionState.Unsupported) return status;

        // Prompting requires an Activity; an Application context cannot show the dialog.
        if (_context() is not Activity activity) return status;

        var code = Interlocked.Increment(ref _nextRequestCode);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[code] = completion;

        activity.RequestPermissions(Map[permission], code);
        return await completion.Task;
    }

    /// <summary>Resolves the pending request that <c>OnRequestPermissionsResult</c> just answered.</summary>
    internal static void Complete(int requestCode, Permission[] grantResults)
    {
        if (!Pending.TryRemove(requestCode, out var completion)) return;

        var granted = grantResults.Any(result => result == Permission.Granted);
        completion.TrySetResult(granted ? CarbonPermissionState.Granted : CarbonPermissionState.Denied);
    }
}
