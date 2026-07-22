import { invoke } from '@dotcarbon/api'

export type BiometricStatus =
    | 'available'
    | 'notEnrolled'
    | 'noHardware'
    | 'unavailable'
    | 'unsupported'

export interface AuthenticateResult {
    success: boolean
    /** Null when success is true. */
    error: string | null
}

/** Whether biometrics can be used right now. Check before prompting so the UI can explain why not. */
export const status = (): Promise<BiometricStatus> => invoke('biometric:status')

/** Prompt for Face ID / Touch ID / BiometricPrompt. */
export const authenticate = (
    options: { title?: string; reason?: string; cancelLabel?: string } = {},
): Promise<AuthenticateResult> =>
    invoke('biometric:authenticate', {
        title: options.title ?? 'Authenticate',
        reason: options.reason ?? 'Confirm your identity',
        cancelLabel: options.cancelLabel ?? 'Cancel',
    })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'biometric:status': { args: void; result: BiometricStatus }
        'biometric:authenticate': {
            args: { title: string; reason: string; cancelLabel: string }
            result: AuthenticateResult
        }
    }
}
