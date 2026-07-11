import { invoke } from '@dotcarbon/api'

export interface UpdaterStatus {
    active: boolean
    currentVersion: string
    endpoints: string[]
    hasPublicKey: boolean
}

export interface UpdateCheckResult<TManifest = unknown> {
    available: boolean
    currentVersion: string
    latestVersion: string | null
    endpoint: string | null
    manifest: TManifest | null
}

export const updater = {
    status: (): Promise<UpdaterStatus> =>
        invoke('updater:status'),

    check: <TManifest = unknown>(endpoint?: string): Promise<UpdateCheckResult<TManifest>> =>
        invoke('updater:check', { endpoint: endpoint ?? null }) as Promise<UpdateCheckResult<TManifest>>,
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'updater:status': { args: void; result: UpdaterStatus }
        'updater:check': { args: { endpoint: string | null }; result: UpdateCheckResult }
    }
}
