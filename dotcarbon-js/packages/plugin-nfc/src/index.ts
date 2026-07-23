import { invoke } from '@dotcarbon/api'

export type NfcStatus =
    /** Hardware present and switched on. */
    | 'available'
    /** Hardware present, but the user has NFC turned off (Android only). */
    | 'disabled'
    /** No NFC hardware — every emulator and simulator, and most desktops. */
    | 'noHardware'
    /** This platform cannot do NFC at all. */
    | 'unsupported'

export interface NfcRecord {
    /** NDEF type-name format, e.g. `WellKnown`, `Mime`, `AbsoluteUri`. */
    typeNameFormat: string
    /** Record type, e.g. `T` for text or `U` for URI. */
    type: string | null
    /** Decoded value for text and URI records; null for anything else. */
    text: string | null
    /** Raw payload bytes, base64-encoded on the wire. */
    payload: string
}

export interface NfcTag {
    /** Hardware serial, when the platform exposes it. iOS NDEF sessions do not. */
    id: string | null
    records: NfcRecord[]
}

/** Whether NFC can be used right now. Check before scanning so the UI can explain why not. */
export const status = (): Promise<NfcStatus> => invoke('nfc:status')

/**
 * Wait for one NDEF tag. Resolves to null if nothing is presented before the timeout, or if the
 * user cancels the scan. `timeoutMs` is clamped to 1s–120s.
 */
export const scan = (
    options: { timeoutMs?: number; prompt?: string } = {},
): Promise<NfcTag | null> =>
    invoke('nfc:scan', {
        timeoutMs: options.timeoutMs ?? 30_000,
        prompt: options.prompt ?? 'Hold your device near the tag',
    })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'nfc:status': { args: void; result: NfcStatus }
        'nfc:scan': {
            args: { timeoutMs: number; prompt: string }
            result: NfcTag | null
        }
    }
}
