#if IOS
using System.Text;
using CoreNFC;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Nfc;
using Foundation;

namespace DotCarbon.Plugins.Nfc.Native;

/// <summary>
/// iOS NFC via <see cref="NFCNdefReaderSession"/>. Unlike Android there is no "is it switched on"
/// notion — iOS either supports reader sessions or it does not — and it only works on a physical
/// device with the NFC reader entitlement, so the simulator always reports no hardware.
/// </summary>
internal sealed class NativeNfcProvider : INfcProvider
{
    public NativeNfcProvider(AppHandle app) { }

    public Task<string> StatusAsync() =>
        Task.FromResult(NFCNdefReaderSession.ReadingAvailable ? NfcStatus.Available : NfcStatus.NoHardware);

    public async Task<NfcTag?> ScanAsync(ScanArgs args)
    {
        if (!NFCNdefReaderSession.ReadingAvailable) return null;

        var completion = new TaskCompletionSource<NfcTag?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new NdefReader(tag => completion.TrySetResult(tag));

        // invalidateAfterFirstRead: one tag per scan, matching ScanAsync's contract.
        var session = new NFCNdefReaderSession(reader, null, invalidateAfterFirstRead: true)
        {
            AlertMessage = args.Prompt,
        };
        session.BeginSession();

        try
        {
            var finished = await Task.WhenAny(completion.Task, Task.Delay(args.TimeoutMs));
            return finished == completion.Task ? await completion.Task : null;
        }
        finally
        {
            session.InvalidateSession();
        }
    }

    private sealed class NdefReader : NFCNdefReaderSessionDelegate
    {
        private readonly Action<NfcTag?> _onTag;

        public NdefReader(Action<NfcTag?> onTag) => _onTag = onTag;

        public override void DidDetect(NFCNdefReaderSession session, NFCNdefMessage[] messages)
        {
            var records = messages
                .SelectMany(message => message.Records ?? [])
                .Select(ToRecord)
                .ToList();
            // iOS does not expose the tag serial to an NDEF reader session.
            _onTag(new NfcTag(null, records));
        }

        public override void DidInvalidate(NFCNdefReaderSession session, NSError error) => _onTag(null);

        private static NfcRecord ToRecord(NFCNdefPayload payload)
        {
            var bytes = payload.Payload?.ToArray() ?? [];
            var type = payload.Type is { } t && t.Length > 0 ? Encoding.UTF8.GetString(t.ToArray()) : null;
            return new NfcRecord(payload.TypeNameFormat.ToString(), type, DecodeText(type, bytes), bytes);
        }

        /// <summary>Mirrors the Android decoding so both platforms hand back the same shape.</summary>
        private static string? DecodeText(string? type, byte[] payload)
        {
            if (payload.Length == 0) return null;

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
