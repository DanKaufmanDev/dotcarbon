import { invoke } from '@dotcarbon/api'

export type Permission =
    | 'camera' | 'microphone' | 'location' | 'notifications' | 'contacts' | 'photoLibrary'

/** 'granted' | 'denied' | 'prompt' (not asked yet) | 'unsupported'. */
export type PermissionState = 'granted' | 'denied' | 'prompt' | 'unsupported'

/** Current state without prompting. Always 'granted' on desktop, which gates none of these. */
export const status = (permission: Permission): Promise<PermissionState> =>
    invoke('permissions:status', { permission })

/** Prompt if undecided, then report the state. Returns immediately if already decided. */
export const request = (permission: Permission): Promise<PermissionState> =>
    invoke('permissions:request', { permission })

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'permissions:status': { args: { permission: Permission }; result: PermissionState }
        'permissions:request': { args: { permission: Permission }; result: PermissionState }
    }
}
