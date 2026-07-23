namespace DotCarbon.Plugins.Nfc;

/// <summary>
/// Fallback when no native provider is registered. Status reports "unsupported" so a UI can hide the
/// feature, and scanning reports rather than hanging on a session that will never start.
/// </summary>
internal sealed class UnsupportedNfcProvider : INfcProvider
{
    public Task<string> StatusAsync() => Task.FromResult(NfcStatus.Unsupported);

    public Task<NfcTag?> ScanAsync(ScanArgs args) =>
        throw new NotSupportedException(
            "NFC is not available on this platform. On Android/iOS call app.UseNfc() " +
            "(DotCarbon.Plugins.Nfc.Native) to register the native provider.");
}
