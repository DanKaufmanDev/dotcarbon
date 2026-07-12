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

export interface UpdateManifest {
    version: string
    target: string
    url: string
    artifact: string | null
    signature: string | null
    publicKey: string | null
    algorithm: string | null
    sha256: string | null
    size: number | null
}

export interface UpdateDownloadResult {
    available: boolean
    currentVersion: string
    latestVersion: string
    path: string
    fileName: string
    sha256: string
    signatureVerified: boolean
    manifest: UpdateManifest
}

export interface InstallUpdateOptions {
    path?: string
    endpoint?: string
    restart?: boolean
}

export interface UpdateInstallResult {
    path: string
    started: boolean
    restartRequested: boolean
    message: string
}

export const updater = {
    status: (): Promise<UpdaterStatus> =>
        invoke('updater:status'),

    check: <TManifest = unknown>(endpoint?: string): Promise<UpdateCheckResult<TManifest>> =>
        invoke('updater:check', { endpoint: endpoint ?? null }) as Promise<UpdateCheckResult<TManifest>>,

    download: (endpoint?: string, destinationDir?: string): Promise<UpdateDownloadResult> =>
        invoke('updater:download', { endpoint: endpoint ?? null, destinationDir: destinationDir ?? null }),

    install: (options: InstallUpdateOptions = {}): Promise<UpdateInstallResult> =>
        invoke('updater:install', {
            path: options.path ?? null,
            endpoint: options.endpoint ?? null,
            restart: options.restart ?? false,
        }),

    installAndRestart: (options: Omit<InstallUpdateOptions, 'restart'> = {}): Promise<UpdateInstallResult> =>
        invoke('updater:install_and_restart', {
            path: options.path ?? null,
            endpoint: options.endpoint ?? null,
            restart: true,
        }),
}

declare module '@dotcarbon/api' {
    interface CarbonCommands {
        'updater:status': { args: void; result: UpdaterStatus }
        'updater:check': { args: { endpoint: string | null }; result: UpdateCheckResult }
        'updater:download': {
            args: { endpoint: string | null; destinationDir: string | null }
            result: UpdateDownloadResult
        }
        'updater:install': {
            args: { path: string | null; endpoint: string | null; restart: boolean }
            result: UpdateInstallResult
        }
        'updater:install_and_restart': {
            args: { path: string | null; endpoint: string | null; restart: boolean }
            result: UpdateInstallResult
        }
    }
}
