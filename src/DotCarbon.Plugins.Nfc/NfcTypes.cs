namespace DotCarbon.Plugins.Nfc;

/// <summary>Whether NFC can be used right now.</summary>
public static class NfcStatus
{
    /// <summary>Hardware present and switched on.</summary>
    public const string Available = "available";
    /// <summary>Hardware present but NFC is turned off in system settings.</summary>
    public const string Disabled = "disabled";
    /// <summary>This device has no NFC hardware (every emulator and simulator).</summary>
    public const string NoHardware = "noHardware";
    /// <summary>This platform/OS version cannot do NFC at all.</summary>
    public const string Unsupported = "unsupported";
}

/// <summary>One NDEF record. <see cref="Text"/> is set when the payload decodes as text or a URI.</summary>
public record NfcRecord(string TypeNameFormat, string? Type, string? Text, byte[] Payload);

/// <summary>A scanned tag. <see cref="Id"/> is the hardware serial when the platform exposes it.</summary>
public record NfcTag(string? Id, IReadOnlyList<NfcRecord> Records);

/// <summary>How long to keep the reader session open waiting for a tag.</summary>
public record ScanArgs(int TimeoutMs = 30_000, string Prompt = "Hold your device near the tag");

/// <summary>
/// Reads NDEF tags. Mobile-only: a mobile app registers the native provider (Android
/// <c>NfcAdapter</c> reader mode / iOS <c>NFCNDEFReaderSession</c>) via <c>app.UseNfc()</c> from
/// <c>DotCarbon.Plugins.Nfc.Native</c>. iOS additionally needs the NFC reader entitlement.
/// </summary>
public interface INfcProvider
{
    Task<string> StatusAsync();

    /// <summary>The next tag presented, or null if none arrives before the timeout.</summary>
    Task<NfcTag?> ScanAsync(ScanArgs args);
}
