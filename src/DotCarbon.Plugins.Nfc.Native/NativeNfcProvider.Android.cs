#if ANDROID
using System.Text;
using Android.App;
using Android.Content;
using Android.Nfc;
using Android.Nfc.Tech;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Nfc;

namespace DotCarbon.Plugins.Nfc.Native;

/// <summary>
/// Android NFC via <see cref="NfcAdapter"/> reader mode, which keeps the scan inside the app instead of
/// bouncing through the system's tag-dispatch intent. Reader mode needs the Activity, so scanning
/// requires the app to be in the foreground — which is also when a user would be holding a tag to it.
/// </summary>
internal sealed class NativeNfcProvider : INfcProvider
{
    private readonly AppHandle _app;

    public NativeNfcProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? Application.Context;

    public Task<string> StatusAsync()
    {
        var adapter = NfcAdapter.GetDefaultAdapter(Context);
        if (adapter is null) return Task.FromResult(NfcStatus.NoHardware);
        return Task.FromResult(adapter.IsEnabled ? NfcStatus.Available : NfcStatus.Disabled);
    }

    public async Task<NfcTag?> ScanAsync(ScanArgs args)
    {
        var adapter = NfcAdapter.GetDefaultAdapter(Context);
        if (adapter is null || !adapter.IsEnabled) return null;
        if (Context is not Activity activity) return null;

        var completion = new TaskCompletionSource<NfcTag?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new ReaderCallback(tag => completion.TrySetResult(tag));

        // NfcA/B/F/V + NDEF covers the common tag families; SkipNdefCheck stays off so NDEF is parsed.
        const NfcReaderFlags flags =
            NfcReaderFlags.NfcA | NfcReaderFlags.NfcB | NfcReaderFlags.NfcF |
            NfcReaderFlags.NfcV | NfcReaderFlags.NfcBarcode;

        activity.RunOnUiThread(() => adapter.EnableReaderMode(activity, callback, flags, null));
        try
        {
            var finished = await Task.WhenAny(completion.Task, Task.Delay(args.TimeoutMs));
            return finished == completion.Task ? await completion.Task : null;
        }
        finally
        {
            activity.RunOnUiThread(() => adapter.DisableReaderMode(activity));
        }
    }

    private sealed class ReaderCallback : Java.Lang.Object, NfcAdapter.IReaderCallback
    {
        private readonly Action<NfcTag?> _onTag;

        public ReaderCallback(Action<NfcTag?> onTag) => _onTag = onTag;

        public void OnTagDiscovered(Tag? tag)
        {
            if (tag is null) { _onTag(null); return; }
            _onTag(new NfcTag(Identifier(tag), ReadRecords(tag)));
        }

        private static string? Identifier(Tag tag) =>
            tag.GetId() is { Length: > 0 } id ? Convert.ToHexString(id) : null;

        private static IReadOnlyList<NfcRecord> ReadRecords(Tag tag)
        {
            var ndef = Ndef.Get(tag);
            if (ndef is null) return [];

            try
            {
                ndef.Connect();
                var message = ndef.NdefMessage;
                if (message?.GetRecords() is not { } records) return [];

                return records
                    .Where(record => record is not null)
                    .Select(record => ToRecord(record!))
                    .ToList();
            }
            catch (Java.IO.IOException)
            {
                return []; // tag moved out of range mid-read
            }
            finally
            {
                try { ndef.Close(); } catch (Java.IO.IOException) { }
            }
        }

        private static NfcRecord ToRecord(NdefRecord record)
        {
            var payload = record.GetPayload() ?? [];
            var type = record.GetTypeInfo() is { Length: > 0 } raw ? Encoding.UTF8.GetString(raw) : null;
            return new NfcRecord(record.Tnf.ToString(), type, DecodeText(record, payload), payload);
        }

        /// <summary>
        /// Well-known text records carry a status byte plus a language code before the text; URI records
        /// carry a prefix index. Anything else is left to the caller as raw bytes.
        /// </summary>
        private static string? DecodeText(NdefRecord record, byte[] payload)
        {
            if (payload.Length == 0) return null;
            var type = record.GetTypeInfo() is { Length: > 0 } raw ? Encoding.UTF8.GetString(raw) : null;

            if (type == "T")
            {
                var languageLength = payload[0] & 0x3F;
                var encoding = (payload[0] & 0x80) != 0 ? Encoding.BigEndianUnicode : Encoding.UTF8;
                var offset = 1 + languageLength;
                return offset <= payload.Length ? encoding.GetString(payload, offset, payload.Length - offset) : null;
            }

            if (type == "U")
                return UriPrefixes[payload[0] < UriPrefixes.Length ? payload[0] : 0] +
                       Encoding.UTF8.GetString(payload, 1, payload.Length - 1);

            return null;
        }

        // NFC Forum URI Record Type Definition, abbreviation table.
        private static readonly string[] UriPrefixes =
        [
            "", "http://www.", "https://www.", "http://", "https://", "tel:", "mailto:",
            "ftp://anonymous:anonymous@", "ftp://ftp.", "ftps://", "sftp://", "smb://", "nfs://",
            "ftp://", "dav://", "news:", "telnet://", "imap:", "rtsp://", "urn:", "pop:", "sip:",
            "sips:", "tftp:", "btspp://", "btl2cap://", "btgoep://", "tcpobex://", "irdaobex://",
            "file://", "urn:epc:id:", "urn:epc:tag:", "urn:epc:pat:", "urn:epc:raw:", "urn:epc:",
            "urn:nfc:",
        ];
    }
}
#endif
