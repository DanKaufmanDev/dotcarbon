#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Biometric;

namespace DotCarbon.Plugins.Biometric.Native;

/// <summary>
/// Android biometrics via the <b>framework</b> <see cref="BiometricPrompt"/> (API 28+). The AndroidX
/// BiometricPrompt would require the host Activity to be a FragmentActivity; the framework one works
/// with any Activity, so the Carbon Activity needs no structural change. API 27 and below report
/// unsupported rather than falling back to the deprecated FingerprintManager.
/// </summary>
internal sealed class NativeBiometricProvider : IBiometricProvider
{
    private readonly AppHandle _app;

    public NativeBiometricProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? Application.Context;

    public Task<string> StatusAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28)) return Task.FromResult(BiometricStatus.Unsupported);

        // BiometricManager.CanAuthenticate() is API 29+; on 28 we can only report that hardware exists.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return Task.FromResult(BiometricStatus.Available);

        var manager = Context.GetSystemService(Context.BiometricService) as BiometricManager;
        if (manager is null) return Task.FromResult(BiometricStatus.Unavailable);

        return Task.FromResult(manager.CanAuthenticate() switch
        {
            BiometricCode.Success => BiometricStatus.Available,
            BiometricCode.ErrorNoneEnrolled => BiometricStatus.NotEnrolled,
            BiometricCode.ErrorNoHardware => BiometricStatus.NoHardware,
            _ => BiometricStatus.Unavailable,
        });
    }

    public async Task<AuthenticateResult> AuthenticateAsync(AuthenticateArgs args)
    {
        var status = await StatusAsync();
        if (status != BiometricStatus.Available)
            return new AuthenticateResult(false, $"Biometrics are not available ({status}).");

        // The prompt is UI, so it needs the Activity rather than an application context.
        if (Context is not Activity activity)
            return new AuthenticateResult(false, "Biometric prompts require the app's Activity.");

        var completion = new TaskCompletionSource<AuthenticateResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        activity.RunOnUiThread(() =>
        {
            try
            {
                var prompt = new BiometricPrompt.Builder(activity)
                    .SetTitle(args.Title)!
                    .SetSubtitle(args.Reason)!
                    // Android requires a negative button when no device-credential fallback is allowed.
                    .SetNegativeButton(args.CancelLabel, activity.MainExecutor!,
                        new DialogClickHandler(() =>
                            completion.TrySetResult(new AuthenticateResult(false, "Cancelled."))))!
                    .Build();

                prompt.Authenticate(
                    new CancellationSignal(),
                    activity.MainExecutor!,
                    new AuthCallback(completion));
            }
            catch (Exception ex)
            {
                completion.TrySetResult(new AuthenticateResult(false, ex.Message));
            }
        });

        return await completion.Task;
    }

    private sealed class AuthCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<AuthenticateResult> _completion;

        public AuthCallback(TaskCompletionSource<AuthenticateResult> completion) => _completion = completion;

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
            _completion.TrySetResult(new AuthenticateResult(true, null));

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            _completion.TrySetResult(new AuthenticateResult(false, errString?.ToString() ?? errorCode.ToString()));

        // Fired for a non-matching finger/face; the prompt stays up, so this is not terminal.
        public override void OnAuthenticationFailed() { }
    }

    private sealed class DialogClickHandler : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        private readonly Action _onClick;

        public DialogClickHandler(Action onClick) => _onClick = onClick;

        public void OnClick(IDialogInterface? dialog, int which) => _onClick();
    }
}
#endif
